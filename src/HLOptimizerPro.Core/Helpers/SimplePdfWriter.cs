using System.Globalization;
using System.Text;

namespace HLOptimizerPro.Core.Helpers;

/// <summary>
/// Gerador de PDF mínimo, sem dependências externas.
///
/// Porque não usamos uma biblioteca? Duas razões práticas:
///   • Instaladores ficam com menos megabytes (importante num utilitário de sistema).
///   • Campos de formulário e JavaScript de PDF são vetores de risco — aqui só escrevemos texto.
///
/// O ficheiro produzido é um PDF 1.4 válido (com tabela xref), legível por qualquer leitor,
/// usando as fontes base Helvetica/Helvetica-Bold com codificação WinAnsi (suporta acentos
/// portugueses: á, ã, ç, ê, ó...).
/// </summary>
public sealed class SimplePdfWriter
{
    // A4 em pontos (72 dpi): 595 x 842.
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double MarginLeft = 48;
    private const double MarginTop = 60;
    private const double MarginBottom = 56;

    private readonly List<StringBuilder> _pages = new();
    private readonly List<string> _currentPageLines = new();

    private double _cursorY = PageHeight - MarginTop;
    private StringBuilder _content = new();

    /// <summary>Define o título do documento (metadados do PDF).</summary>
    public string Title { get; set; } = "Relatório";

    public string Author { get; set; } = AppConstants.ProductName;

    /// <summary>Escreve uma linha de texto simples.</summary>
    public void AddLine(string text, double fontSize = 10, bool bold = false, double spacingAfter = 4)
    {
        EnsureSpace(fontSize + spacingAfter);
        WriteText(text, MarginLeft, _cursorY, fontSize, bold);
        _cursorY -= fontSize + spacingAfter;
    }

    /// <summary>Escreve um título de secção com uma linha por baixo.</summary>
    public void AddHeading(string text, int level = 1)
    {
        var size = level == 1 ? 17 : 12.5;
        EnsureSpace(size + 16);

        if (level == 1) _cursorY -= 8;

        WriteText(text, MarginLeft, _cursorY, size, bold: true);
        _cursorY -= size + 6;

        WriteLine(MarginLeft, _cursorY, PageWidth - MarginLeft * 2, 0.6);
        _cursorY -= 12;
    }

    /// <summary>Escreve uma linha com etiqueta à esquerda e valor à direita (usada em tabelas simples).</summary>
    public void AddKeyValue(string label, string value, double fontSize = 10)
    {
        EnsureSpace(fontSize + 4);
        WriteText(label, MarginLeft, _cursorY, fontSize, bold: true);
        WriteText(value, MarginLeft + 190, _cursorY, fontSize, bold: false);
        _cursorY -= fontSize + 5;
    }

    /// <summary>Cria uma linha horizontal de separação.</summary>
    public void AddSeparator(double thickness = 0.5)
    {
        EnsureSpace(10);
        WriteLine(MarginLeft, _cursorY, PageWidth - MarginLeft * 2, thickness);
        _cursorY -= 10;
    }

    public void AddSpace(double height = 10)
    {
        EnsureSpace(height);
        _cursorY -= height;
    }

    /// <summary>Guarda o PDF no caminho indicado.</summary>
    public void Save(string path)
    {
        FlushPage();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllBytes(path, Build());
    }

    /// <summary>Devolve o PDF em memória (para enviar/abrir sem escrever em disco).</summary>
    public byte[] ToArray()
    {
        FlushPage();
        return Build();
    }

    // ------------------------------------------------------------------ internos

    private void EnsureSpace(double needed)
    {
        if (_cursorY - needed >= MarginBottom) return;

        FlushPage();
        _cursorY = PageHeight - MarginTop;
    }

    private void FlushPage()
    {
        if (_content.Length == 0) return;

        _pages.Add(_content);
        _content = new StringBuilder();
    }

    private void WriteText(string text, double x, double y, double fontSize, bool bold)
    {
        // A codificação WinAnsi é escrita byte a byte; o conteúdo é acumulado como latin1.
        var font = bold ? "/F2" : "/F1";
        var escaped = Escape(text);

        _content.Append(CultureInfo.InvariantCulture,
            $"BT {font} {fontSize:0.##} Tf 1 0 0 1 {x:0.##} {y:0.##} Tm ({escaped}) Tj ET\n");
    }

    private void WriteLine(double x, double y, double width, double thickness)
    {
        _content.Append(CultureInfo.InvariantCulture, $"0.75 0.75 0.8 RG {thickness:0.##} w {x:0.##} {y:0.##} m {x + width:0.##} {y:0.##} l S\n");
    }

    /// <summary>Escapa caracteres com significado em strings PDF.</summary>
    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 8);

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '(': builder.Append("\\("); break;
                case ')': builder.Append("\\)"); break;
                case '\\': builder.Append("\\\\"); break;
                case '\r': break;
                case '\n': builder.Append(' '); break;
                default:
                    // Caracteres fora do latin1 (emoji, por exemplo) são substituídos por "?".
                    builder.Append(ch <= 255 ? ch : '?');
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Constrói o ficheiro PDF completo (objetos, xref e trailer).</summary>
    private byte[] Build()
    {
        var writer = new MemoryStream();
        var offsets = new List<long> { 0 };   // índice 0 corresponde ao objeto livre

        void Write(string text)
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            writer.Write(bytes, 0, bytes.Length);
        }

        long BeginObject(int number)
        {
            var position = writer.Position;
            while (offsets.Count <= number) offsets.Add(0);
            offsets[number] = position;
            Write($"{number} 0 obj\n");
            return position;
        }

        void EndObject() => Write("endobj\n");

        Write("%PDF-1.4\n");
        Write("%âãÏÓ\n");   // marca binária (evita corrupção em transferências de texto)

        // Objeto 1: catálogo • Objeto 2: páginas
        var pageObjects = new List<int>();
        var nextObject = 3;

        Write($"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesObjectNumber = 2;
        var pageNumbers = new List<int>();

        // Cada página usa dois objetos: página + conteúdo.
        foreach (var pageContent in _pages)
        {
            var contentBytes = Encoding.Latin1.GetBytes(pageContent.ToString());
            var pageNumber = nextObject++;
            var contentNumber = nextObject++;

            BeginObject(pageNumber);
            Write($"<< /Type /Page /Parent {pagesObjectNumber} 0 R /MediaBox [0 0 {PageWidth:0.##} {PageHeight:0.##}] " +
                  $"/Resources << /Font << /F1 {3} 0 R /F2 {4} 0 R >> >> /Contents {contentNumber} 0 R >>\n");
            EndObject();

            BeginObject(contentNumber);
            Write($"<< /Length {contentBytes.Length} >>\nstream\n");
            writer.Write(contentBytes, 0, contentBytes.Length);
            Write("endstream\n");
            EndObject();

            pageNumbers.Add(pageNumber);
        }

        // Objeto 2: lista de páginas (escrito agora, com o número conhecido).
        BeginObject(pagesObjectNumber);
        Write($"<< /Type /Pages /Count {pageNumbers.Count} /Kids [{string.Join(" ", pageNumbers.Select(n => $"{n} 0 R"))}] >>\n");
        EndObject();

        // Objetos 3 e 4: fontes base.
        BeginObject(3);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\n");
        EndObject();

        BeginObject(4);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\n");
        EndObject();

        // Objeto de informação (metadados) — 5.
        BeginObject(5);
        Write($"<< /Title ({Escape(Title)}) /Author ({Escape(Author)}) /Producer ({Escape(AppConstants.ProductName + " " + AppConstants.Version)}) " +
              $"/CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>\n");
        EndObject();

        // Tabela xref.
        var xrefPosition = writer.Position;
        var objectCount = offsets.Count;

        Write($"xref\n0 {objectCount}\n");
        Write("0000000000 65535 f \n");

        for (var i = 1; i < objectCount; i++)
        {
            Write($"{offsets[i]:D10} 00000 n \n");
        }

        Write($"trailer\n<< /Size {objectCount} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xrefPosition}\n%%EOF\n");

        writer.Flush();
        return writer.ToArray();
    }
}
