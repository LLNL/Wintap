using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.etl.shared
{
    internal class CertificateManager
    {
        internal enum CertificateTypeEnum { File, Installed, Unknown }

        private string storeName;

        internal CertificateTypeEnum certificateType;

        internal X509Certificate2 deviceCertificate;

        internal X509Certificate2 rootCertificate;

        /// <summary>
        /// Finds the first certificate matching the given name suffix string at the given store.
        /// </summary>
        /// <param name="_storeName"></param>
        /// <param name="certificateNameSuffix"></param>
        /// <exception cref="Exception"></exception>
        internal CertificateManager(string _storeName, string _certificateNameSuffix) 
        {
            // we support certificates installed to the personal folder of the local machine store or PFX files on disk
            this.storeName = _storeName;
            certificateType = CertificateTypeEnum.Unknown;
            DirectoryInfo directoryInfo = new DirectoryInfo(this.storeName);
            if(directoryInfo.Exists)
            {
                resolvePfxCert(_certificateNameSuffix);
            }
            else
            {
                deviceCertificate = resolveInstalledCert(_certificateNameSuffix);
            }

            if (deviceCertificate == null)
            {
                throw new Exception("CERTIFICATE NOT FOUND");
            }
        }

        // resolves the FIRST .pfx certificate file found at the 'storeName' matching the given suffix
        private void resolvePfxCert(string certificateNameSuffix)
        {
            if (!certificateNameSuffix.EndsWith(".pfx"))
            {
                certificateNameSuffix += "*.pfx";
            }
            DirectoryInfo storeDir = new DirectoryInfo(this.storeName);
            if(storeDir.Exists)
            {
                if(storeDir.GetFileSystemInfos(certificateNameSuffix).Length > 0)
                {
                    deviceCertificate = new X509Certificate2(storeDir.GetFileSystemInfos(certificateNameSuffix).First().FullName, "");
                    certificateType = CertificateTypeEnum.File;
                }
            }
        }

        private X509Certificate2 resolveInstalledCert(string certNameSuffix)
        {
            X509Certificate2 cert = null;
            X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            foreach (X509Certificate2 certificate in store.Certificates)
            {
                if (certificate.Subject.EndsWith(certNameSuffix))
                {
                    cert = certificate;
                    certificateType = CertificateTypeEnum.Installed;
                    break;
                }
            }
            store.Close();
            return cert;
        }
    }
}
