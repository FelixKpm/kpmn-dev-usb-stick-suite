using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AEGIS
{
    // Ein einzelner Datei-Eintrag im Vault, wie er in der UI angezeigt wird
    public sealed class VaultListEntry
    {
        public string Name { get; }
        public long Size { get; }
        public DateTime Modified { get; }

        public VaultListEntry(string name, long size, DateTime modified)
        {
            Name = name;
            Size = size;
            Modified = modified;
        }

        public override string ToString()
        {
            var sizeKb = Size / 1024.0;
            var sizeText = sizeKb < 1024 ? $"{sizeKb:0.#} KB" : $"{sizeKb / 1024:0.#} MB";
            return $"📄 {Name}  ({sizeText})";
        }
    }

    // Verwaltet einen einzelnen verschlüsselten Vault-Container (Dateiname + Inhalt beide verschlüsselt).
    // Format: "KVLT" | Version(1) | Salt(16) | Iterations(4) | Nonce(12) | Tag(16) | Ciphertext(...)
    // Ciphertext ist ein AES-256-GCM-verschlüsseltes ZIP-Archiv, das die eigentlichen Dateien enthält.
    public sealed class VaultService
    {
        private const string Magic = "KVLT";
        private const int SaltSize = 16;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int Iterations = 200_000;

        private readonly string _filePath;
        private byte[]? _key;
        private byte[]? _zipBytes;

        public VaultService(string filePath)
        {
            _filePath = filePath;
        }

        public bool IsUnlocked => _key != null && _zipBytes != null;

        public bool VaultExists() => File.Exists(_filePath);

        // Legt einen neuen, leeren Vault mit dem angegebenen Master-Passwort an
        public void CreateNew(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            _key = DeriveKey(password, salt, Iterations);

            using var ms = new MemoryStream();
            using (new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                // leeres Archiv
            }
            _zipBytes = ms.ToArray();

            SaveInternal(salt, Iterations);
        }

        // Entschlüsselt den Vault mit dem angegebenen Passwort.
        // Wirft CryptographicException bei falschem Passwort oder beschädigter Datei.
        public void Unlock(string password)
        {
            var (salt, iterations, nonce, tag, ciphertext) = ReadContainer();
            var key = DeriveKey(password, salt, iterations);

            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            _key = key;
            _zipBytes = plaintext;
        }

        // Löscht den Schlüssel und die entschlüsselten Daten aus dem Speicher
        public void Lock()
        {
            if (_key != null) Array.Clear(_key);
            if (_zipBytes != null) Array.Clear(_zipBytes);
            _key = null;
            _zipBytes = null;
        }

        public List<VaultListEntry> ListEntries()
        {
            EnsureUnlocked();
            using var ms = new MemoryStream(_zipBytes!);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            return archive.Entries
                .Select(e => new VaultListEntry(e.FullName, e.Length, e.LastWriteTime.DateTime))
                .OrderBy(e => e.Name)
                .ToList();
        }

        // Fügt eine Datei von der Festplatte unter dem angegebenen Namen in den Vault ein (überschreibt bei Namensgleichheit)
        public void AddFile(string sourceFilePath, string entryName)
        {
            EnsureUnlocked();

            using var ms = new MemoryStream();
            ms.Write(_zipBytes!, 0, _zipBytes!.Length);
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Update, true))
            {
                archive.GetEntry(entryName)?.Delete();
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var sourceStream = File.OpenRead(sourceFilePath);
                sourceStream.CopyTo(entryStream);
            }

            _zipBytes = ms.ToArray();
            Save();
        }

        public void RemoveEntry(string entryName)
        {
            EnsureUnlocked();

            using var ms = new MemoryStream();
            ms.Write(_zipBytes!, 0, _zipBytes!.Length);
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Update, true))
            {
                archive.GetEntry(entryName)?.Delete();
            }

            _zipBytes = ms.ToArray();
            Save();
        }

        public byte[] ExtractEntry(string entryName)
        {
            EnsureUnlocked();

            using var ms = new MemoryStream(_zipBytes!);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = archive.GetEntry(entryName) ?? throw new FileNotFoundException($"Eintrag nicht gefunden: {entryName}");

            using var entryStream = entry.Open();
            using var outMs = new MemoryStream();
            entryStream.CopyTo(outMs);
            return outMs.ToArray();
        }

        // Verschlüsselt den aktuellen Stand erneut und schreibt ihn auf die Platte (Salt/Iterations bleiben unverändert)
        private void Save()
        {
            var (salt, iterations, _, _, _) = ReadContainer();
            SaveInternal(salt, iterations);
        }

        private void SaveInternal(byte[] salt, int iterations)
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[_zipBytes!.Length];
            var tag = new byte[TagSize];

            using var aes = new AesGcm(_key!, TagSize);
            aes.Encrypt(nonce, _zipBytes, ciphertext, tag);

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            using var fs = new FileStream(_filePath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fs);
            writer.Write(Encoding.ASCII.GetBytes(Magic));
            writer.Write((byte)1);
            writer.Write(salt);
            writer.Write(iterations);
            writer.Write(nonce);
            writer.Write(tag);
            writer.Write(ciphertext);
        }

        private (byte[] salt, int iterations, byte[] nonce, byte[] tag, byte[] ciphertext) ReadContainer()
        {
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (magic != Magic)
                throw new InvalidDataException(Loc.T("Vault.InvalidFile"));

            reader.ReadByte(); // Version, aktuell ungenutzt
            var salt = reader.ReadBytes(SaltSize);
            var iterations = reader.ReadInt32();
            var nonce = reader.ReadBytes(NonceSize);
            var tag = reader.ReadBytes(TagSize);
            var ciphertext = reader.ReadBytes((int)(fs.Length - fs.Position));

            return (salt, iterations, nonce, tag, ciphertext);
        }

        private static byte[] DeriveKey(string password, byte[] salt, int iterations)
            => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

        private void EnsureUnlocked()
        {
            if (!IsUnlocked)
                throw new InvalidOperationException("Vault ist gesperrt.");
        }
    }
}
