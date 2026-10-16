using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Reine Syntaxprüfung (Parser) aller .cs-Dateien unter den Argument-Ordnern – ohne Referenzen.
var options = new CSharpParseOptions(LanguageVersion.Preview);
var errors = 0;
foreach (var root in args)
{
    foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/")))
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, file);
        foreach (var d in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            var pos = d.Location.GetLineSpan().StartLinePosition;
            Console.WriteLine($"{file}({pos.Line + 1},{pos.Character + 1}): {d.Id} {d.GetMessage()}");
            errors++;
        }
    }
}

Console.WriteLine(errors == 0 ? "Syntax ok" : $"{errors} Syntaxfehler");
return errors == 0 ? 0 : 1;
