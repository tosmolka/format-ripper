﻿using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;

namespace JetBrains.SignatureVerifier.Crypt
{
    public class CrlCacheFileSystem
    {
        private readonly string _cacheDir;
        private readonly X509CrlParser _crlParser = new X509CrlParser();

        public CrlCacheFileSystem([NotNull] string cacheDir = "crlscache")
        {
            var cacheDirName = cacheDir ?? throw new ArgumentNullException(nameof(cacheDir));
            _cacheDir = Path.Combine(Path.GetTempPath(), cacheDirName);
        }

        public List<X509Crl> GetCrls([NotNull] string issuerId)
        {
            if (issuerId == null) throw new ArgumentNullException(nameof(issuerId));

            var crlFiles = getCrlFiles(issuerId);
            var res = new List<X509Crl>();

            foreach (var path in crlFiles)
            {
                using var stream = File.OpenRead(path);
                var crl = _crlParser.ReadCrl(stream);
                res.Add(crl);
            }

            return res;
        }

        public void UpdateCrls(string issuerId, List<byte[]> crlsData)
        {
            cleanUpCrls(issuerId);
            saveCrls(issuerId, crlsData);
        }

        private IEnumerable<string> getCrlFiles(string issuerId)
        {
            ensureCacheDirectory();
            return Directory.EnumerateFiles(_cacheDir, $"{issuerId}*.crl", SearchOption.TopDirectoryOnly);
        }

        private void ensureCacheDirectory()
        {
            if (!Directory.Exists(_cacheDir))
                Directory.CreateDirectory(_cacheDir);
        }

        private void cleanUpCrls(string issuerId)
        {
            foreach (var crlFile in getCrlFiles(issuerId))
                File.Delete(crlFile);
        }

        private void saveCrls(string issuerId, List<byte[]> crlsData)
        {
            if (crlsData.Count == 1)
            {
                var crlFileName = $"{issuerId}.crl";
                saveCrl(crlFileName, crlsData[0]);
            }
            else
            {
                for (var i = 0; i < crlsData.Count; i++)
                {
                    var crlFileName = $"{issuerId}_{i}.crl";
                    saveCrl(crlFileName, crlsData[i]);
                }
            }
        }

        private void saveCrl(string crlFileName, byte[] crlData)
        {
            var crlFilePath = Path.Combine(_cacheDir, crlFileName);
            File.WriteAllBytes(crlFilePath, crlData);
        }
    }
}