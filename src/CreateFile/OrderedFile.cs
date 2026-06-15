// OrderedFile.cs
// Requires NuGet packages:
//   dotnet add package SixLabors.ImageSharp
//   dotnet add package PdfSharpCore

namespace CreateFile
{
    public class OrderedFile
    {
        public string FileName { get; set; }
        public int Order { get; set; }
    }
}