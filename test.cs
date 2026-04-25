using System;
using System.Diagnostics;

class Program
{
    static void Main()
    {
        string exePath = @"C:\Program Files\App\app.exe";
        var psi = new ProcessStartInfo();
        psi.ArgumentList.Add("/create");
        psi.ArgumentList.Add("/tr");
        psi.ArgumentList.Add($"\"{exePath}\"");
        
        Console.WriteLine("ArgumentList test (not directly printable, but we can see Arguments if we build it):");
        // We can't print ArgumentList directly as a single string easily in .NET 6+ without reflection or internal methods.
        
        string args = $"/create /tn \"Task\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f";
        Console.WriteLine(args);
    }
}
