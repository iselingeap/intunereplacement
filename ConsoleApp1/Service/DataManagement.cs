using ConsoleApp1.Entiteiten;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1.Service
{

    public class Datamanagement 
    {
        public DevicesData convertJsonToSql(string json)
        {
    
            DevicesData data = JsonConvert.DeserializeObject<DevicesData>(json);
            return data;
        }


        public string EncryptString(string plainText, string key)
        {
            using var aes = Aes.Create();
            var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            aes.Key = keyBytes;
            aes.GenerateIV();
            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);
            return Convert.ToBase64String(result);
        }

        public string DecryptString(string cipherText, string key)
        {
            var fullCipher = Convert.FromBase64String(cipherText);
            using var aes = Aes.Create();
            var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            aes.Key = keyBytes;
            var iv = new byte[aes.BlockSize / 8];
            var cipherBytes = new byte[fullCipher.Length - iv.Length];
            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, cipherBytes, 0, cipherBytes.Length);
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
    }

    public sealed class SslTcpServer
    {
        static X509Certificate serverCertificate = null;
        // The certificate parameter specifies the name of the file
        // containing the machine certificate.
        public static void RunServer(string certificate)
        {
            serverCertificate = X509Certificate.CreateFromCertFile(certificate);
            // Create a TCP/IP (IPv4) socket and listen for incoming connections.
            TcpListener listener = new TcpListener(IPAddress.Any, 5000);
            listener.Start();
            while (true)
            {
                Console.WriteLine("Waiting for a client to connect...");
                // Application blocks while waiting for an incoming connection.
                // Type CNTL-C to terminate the server.
                TcpClient client = listener.AcceptTcpClient();
                ProcessClient(client);
            }
        }
        static void ProcessClient(TcpClient client)
        {
            // A client has connected. Create the
            // SslStream using the client's network stream.
            SslStream sslStream = new SslStream(
                client.GetStream(), false);
            // Authenticate the server but don't require the client to authenticate.
            try
            {
                sslStream.AuthenticateAsServer(serverCertificate, clientCertificateRequired: false, checkCertificateRevocation: true);

                // Display the properties and settings for the authenticated stream.
                DisplaySecurityLevel(sslStream);
                DisplaySecurityServices(sslStream);
                DisplayCertificateInformation(sslStream);
                DisplayStreamProperties(sslStream);

                // Set timeouts for the read and write to 5 seconds.
                sslStream.ReadTimeout = 5000;
                sslStream.WriteTimeout = 5000;
                // Read a message from the client.
                Console.WriteLine("Waiting for client message...");
                string messageData = ReadMessage(sslStream);
                Console.WriteLine("Received: {0}", messageData);

                // Write a message to the client.
                byte[] message = Encoding.UTF8.GetBytes("Hello from the server.<EOF>");
                Console.WriteLine("Sending hello message.");
                sslStream.Write(message);
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine("Exception: {0}", e.Message);
                if (e.InnerException != null)
                {
                    Console.WriteLine("Inner exception: {0}", e.InnerException.Message);
                }
                Console.WriteLine("Authentication failed - closing the connection.");
                sslStream.Close();
                client.Close();
                return;
            }
            finally
            {
                // The client stream will be closed with the sslStream
                // because we specified this behavior when creating
                // the sslStream.
                sslStream.Close();
                client.Close();
            }
        }
        static string ReadMessage(SslStream sslStream)
        {
            // Read the  message sent by the client.
            // The client signals the end of the message using the
            // "<EOF>" marker.
            byte[] buffer = new byte[2048];
            StringBuilder messageData = new StringBuilder();
            int bytes = -1;
            do
            {
                // Read the client's test message.
                bytes = sslStream.Read(buffer, 0, buffer.Length);

                // Use Decoder class to convert from bytes to UTF8
                // in case a character spans two buffers.
                Decoder decoder = Encoding.UTF8.GetDecoder();
                char[] chars = new char[decoder.GetCharCount(buffer, 0, bytes)];
                decoder.GetChars(buffer, 0, bytes, chars, 0);
                messageData.Append(chars);
                // Check for EOF or an empty message.
                if (messageData.ToString().IndexOf("<EOF>") != -1)
                {
                    break;
                }
            } while (bytes != 0);

            return messageData.ToString();
        }
        static void DisplaySecurityLevel(SslStream stream)
        {
            Console.WriteLine("Cipher: {0} strength {1}", stream.CipherAlgorithm, stream.CipherStrength);
            Console.WriteLine("Hash: {0} strength {1}", stream.HashAlgorithm, stream.HashStrength);
            Console.WriteLine("Key exchange: {0} strength {1}", stream.KeyExchangeAlgorithm, stream.KeyExchangeStrength);
            Console.WriteLine("Protocol: {0}", stream.SslProtocol);
        }
        static void DisplaySecurityServices(SslStream stream)
        {
            Console.WriteLine("Is authenticated: {0} as server? {1}", stream.IsAuthenticated, stream.IsServer);
            Console.WriteLine("IsSigned: {0}", stream.IsSigned);
            Console.WriteLine("Is Encrypted: {0}", stream.IsEncrypted);
            Console.WriteLine("Is mutually authenticated: {0}", stream.IsMutuallyAuthenticated);
        }
        static void DisplayStreamProperties(SslStream stream)
        {
            Console.WriteLine("Can read: {0}, write {1}", stream.CanRead, stream.CanWrite);
            Console.WriteLine("Can timeout: {0}", stream.CanTimeout);
        }
        static void DisplayCertificateInformation(SslStream stream)
        {
            Console.WriteLine("Certificate revocation list checked: {0}", stream.CheckCertRevocationStatus);

            X509Certificate localCertificate = stream.LocalCertificate;
            if (stream.LocalCertificate != null)
            {
                Console.WriteLine("Local cert was issued to {0} and is valid from {1} until {2}.",
                    localCertificate.Subject,
                    localCertificate.GetEffectiveDateString(),
                    localCertificate.GetExpirationDateString());
            }
            else
            {
                Console.WriteLine("Local certificate is null.");
            }
            // Display the properties of the client's certificate.
            X509Certificate remoteCertificate = stream.RemoteCertificate;
            if (stream.RemoteCertificate != null)
            {
                Console.WriteLine("Remote cert was issued to {0} and is valid from {1} until {2}.",
                    remoteCertificate.Subject,
                    remoteCertificate.GetEffectiveDateString(),
                    remoteCertificate.GetExpirationDateString());
            }
            else
            {
                Console.WriteLine("Remote certificate is null.");
            }
        }
        private static void DisplayUsage()
        {
            Console.WriteLine("To start the server specify:");
            Console.WriteLine("serverSync certificateFile.cer");
            Environment.Exit(1);
        }
        public static int Main(string[] args)
        {
            string certificate = null;
            if (args == null || args.Length < 1)
            {
                DisplayUsage();
            }
            certificate = args[0];
            SslTcpServer.RunServer(certificate);
            return 0;
        }
    }
}


