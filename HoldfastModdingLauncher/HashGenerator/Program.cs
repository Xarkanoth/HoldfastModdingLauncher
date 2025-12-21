using System;
using System.Security.Cryptography;
using System.Text;

string password = "CSGDominance";
string salt = "HF_MODDING_2024_XARK";

using (var sha256 = SHA256.Create())
{
    string saltedPassword = password + salt;
    byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
    
    var sb = new StringBuilder();
    foreach (byte b in bytes)
    {
        sb.Append(b.ToString("x2"));
    }
    Console.WriteLine($"Password: {password}");
    Console.WriteLine($"Salt: {salt}");
    Console.WriteLine($"Hash: {sb}");
}
