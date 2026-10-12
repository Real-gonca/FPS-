using System.Text;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Exportação do relatório de avaliação em HTML (tema escuro, imprimível) e PDF.
///
/// O relatório inclui sempre:
///   • pontuação global e sub-pontuações com os critérios medidos,
///   • diagnóstico e recomendações,
///   • hardware detetado,
///   • lista das otimizações já aplicadas (diário) — transparência total sobre o que foi alterado.
///
/// Os ficheiros ficam em <c>%AppData%\HLOptimizerPro\relatorios</c>.
/// Nota: o HTML gerado usa aspas simples nos atributos, o que permite mantê-lo dentro de
/// strings verbatim de C# sem qualquer necessidade de escape.
/// </summary>
public sealed class ReportService
{
    private readonly ILogger<ReportService> _logger;
    private readonly OptimizationJournal _journal;

    public ReportService(ILogger<ReportService> logger, OptimizationJournal journal)
    {
        _logger = logger;
        _journal = journal;
    }

    /// <summary>Exporta o relatório em HTML e devolve o caminho do ficheiro criado.</summary>
    public Result<string> ExportHtml(AssessmentReport report, string? destinationFile = null)
    {
        try
        {
            var path = destinationFile ?? BuildPath("html");
            File.WriteAllText(path, BuildHtml(report), Encoding.UTF8);

            _logger.LogInformation("Relatório HTML exportado para {Path}", path);
            return Result<string>.Ok(path, $"Relatório exportado para {path}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao exportar o relatório HTML.");
            return Result<string>.Fail("Não foi possível exportar o relatório.", ex.Message);
        }
    }

    /// <summary>Exporta o relatório em PDF (gerador próprio, sem dependências externas).</summary>
    public Result<string> ExportPdf(AssessmentReport report, string? destinationFile = null)
    {
        try
        {
            var path = destinationFile ?? BuildPath("pdf");

            var pdf = new SimplePdfWriter
            {
                Title = $"Relatório de otimização — {AppConstants.ProductName}",
                Author = AppConstants.Publisher,
            };

            pdf.AddHeading(AppConstants.ProductName);
            pdf.AddLine($"Relatório de avaliação do sistema — {report.GeneratedAt:dd/MM/yyyy HH:mm}", 9.5, spacingAfter: 14);

            pdf.AddHeading("Pontuação global", 2);
            pdf.AddKeyValue("Pontuação global", $"{report.OverallScoreText} ({report.Grade})");
            pdf.AddLine(report.Diagnosis, 9.5, spacingAfter: 12);

            pdf.AddHeading("Sub-pontuações", 2);
            foreach (var breakdown in report.Breakdowns)
            {
                pdf.AddKeyValue($"{breakdown.Name} (peso {breakdown.Weight:P0})", $"{breakdown.ScoreText} — {breakdown.GradeText}");
            }

            pdf.AddSpace(6);
            pdf.AddHeading("Critérios medidos", 2);

            foreach (var breakdown in report.Breakdowns)
            {
                pdf.AddLine(breakdown.Name.ToUpperInvariant(), 10, bold: true, spacingAfter: 3);

                foreach (var criterion in breakdown.Criteria)
                {
                    var penalty = criterion.Penalty > 0 ? $"−{criterion.Penalty:N1} pts" : "sem penalização";
                    pdf.AddLine($"   • {criterion.Name}: {criterion.MeasuredValue} ({penalty})", 9, spacingAfter: 2);
                }

                pdf.AddSpace(6);
            }

            if (report.Hardware is { } hardware)
            {
                pdf.AddHeading("Hardware detetado", 2);
                pdf.AddKeyValue("Processador", hardware.CpuName);
                pdf.AddKeyValue("Núcleos", Formatters.Cores(hardware.CpuCores, hardware.CpuThreads));
                pdf.AddKeyValue("Memória", Formatters.Bytes(hardware.TotalRamBytes));
                pdf.AddKeyValue("Placa gráfica", hardware.GpuName);
                pdf.AddKeyValue("Armazenamento", $"{hardware.StorageType} — {hardware.StorageName} ({Formatters.Bytes(hardware.StorageTotalBytes)})");
                pdf.AddKeyValue("Sistema", $"{hardware.OperatingSystem} (build {hardware.OsBuild})");
                pdf.AddSpace(10);
            }

            if (report.Recommendations.Count > 0)
            {
                pdf.AddHeading("Recomendações", 2);
                foreach (var recommendation in report.Recommendations.Take(20))
                {
                    pdf.AddLine($"[{recommendation.PriorityText}] {recommendation.Title}", 9.5, bold: true, spacingAfter: 2);
                    pdf.AddLine($"   {recommendation.Description}", 9, spacingAfter: 6);
                }
            }

            var applied = _journal.ActiveEntries;
            if (applied.Count > 0)
            {
                pdf.AddHeading("Otimizações aplicadas neste computador", 2);
                foreach (var entry in applied)
                {
                    pdf.AddLine($"• {entry.Title} — {entry.AppliedAt:dd/MM/yyyy HH:mm}", 9, spacingAfter: 2);
                }

                pdf.AddSpace(8);
                pdf.AddLine("Todas as alterações acima podem ser revertidas em «Otimizações → Reverter tudo».", 9);
            }

            pdf.Save(path);

            _logger.LogInformation("Relatório PDF exportado para {Path}", path);
            return Result<string>.Ok(path, $"Relatório PDF exportado para {path}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao exportar o relatório PDF.");
            return Result<string>.Fail("Não foi possível gerar o PDF.", ex.Message);
        }
    }

    /// <summary>Nome sugerido para o ficheiro (dentro da pasta de relatórios).</summary>
    public static string BuildPath(string extension)
    {
        AppPaths.EnsureCreated();
        var name = $"HL-Relatorio-{DateTime.Now:yyyyMMdd-HHmm}.{extension}";
        return Path.Combine(AppPaths.Reports, name);
    }

    // ------------------------------------------------------------------ HTML

    private string BuildHtml(AssessmentReport report)
    {
        var cards = new StringBuilder();
        var criteriaRows = new StringBuilder();

        foreach (var breakdown in report.Breakdowns)
        {
            var color = breakdown.Health switch
            {
                HealthLevel.Otimo => "#21C97A",
                HealthLevel.Atencao => "#F2B93B",
                _ => "#FF4D5E",
            };

            cards.Append($@"
        <div class='card'>
          <div class='card-title'>{breakdown.Name} <span class='weight'>peso {breakdown.Weight:P0}</span></div>
          <div class='bar'><div class='bar-fill' style='width:{breakdown.Score:0.#}%;background:{color}'></div></div>
          <div class='card-value'>{breakdown.ScoreText} — {breakdown.GradeText}</div>
          <div class='muted'>{breakdown.Description}</div>
        </div>");

            foreach (var criterion in breakdown.Criteria)
            {
                var penalty = criterion.Penalty > 0 ? $"−{criterion.Penalty:N1} pts" : "—";
                criteriaRows.Append($@"
            <tr>
              <td>{breakdown.Name}</td>
              <td>{criterion.Name}</td>
              <td>{criterion.MeasuredValue}</td>
              <td class='{(criterion.IsProblem ? "bad" : "ok")}'>{penalty}</td>
              <td>{criterion.Advice ?? string.Empty}</td>
            </tr>");
            }
        }

        var recommendations = string.Join(Environment.NewLine, report.Recommendations.Select(r => $@"
        <li>
          <strong>{r.Title}</strong> <span class='tag tag-{r.Priority.ToString().ToLowerInvariant()}'>{r.PriorityText}</span>
          <div class='muted'>{r.Description}</div>
          <div class='benefit'>Ganho esperado: {r.ExpectedBenefit}</div>
        </li>"));

        var applied = _journal.ActiveEntries.Count == 0
            ? "<li class='muted'>Nenhuma otimização aplicada por esta aplicação neste computador.</li>"
            : string.Join(Environment.NewLine, _journal.ActiveEntries.Select(e =>
                $"<li>{e.Title} <span class='muted'>— {e.AppliedAt:dd/MM/yyyy HH:mm}{(e.RequiresRestart ? " (após reinício)" : string.Empty)}</span></li>"));

        var hardware = report.Hardware is null ? string.Empty : $@"
      <h2>Hardware detetado</h2>
      <table>
        <tr><th>Processador</th><td>{report.Hardware.CpuName}</td></tr>
        <tr><th>Núcleos</th><td>{Formatters.Cores(report.Hardware.CpuCores, report.Hardware.CpuThreads)}</td></tr>
        <tr><th>Memória</th><td>{Formatters.Bytes(report.Hardware.TotalRamBytes)}</td></tr>
        <tr><th>Placa gráfica</th><td>{report.Hardware.GpuName}</td></tr>
        <tr><th>Armazenamento</th><td>{report.Hardware.StorageType} — {report.Hardware.StorageName} ({Formatters.Bytes(report.Hardware.StorageTotalBytes)})</td></tr>
        <tr><th>Sistema</th><td>{report.Hardware.OperatingSystem} (build {report.Hardware.OsBuild})</td></tr>
      </table>";

        return $@"<!DOCTYPE html>
<html lang='pt'>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<title>Relatório {AppConstants.ProductName} — {report.GeneratedAt:dd/MM/yyyy HH:mm}</title>
<style>
  :root {{ --bg:#070D1F; --panel:#0B1530; --accent:#1E6BFF; --text:#E6EDF7; --muted:#8FA3C8; }}
  * {{ box-sizing:border-box; }}
  body {{ margin:0; padding:32px; background:linear-gradient(160deg,#070D1F,#0B1530 60%,#070D1F);
         color:var(--text); font-family:'Segoe UI Variable','Segoe UI',system-ui,sans-serif; }}
  h1 {{ font-size:26px; margin:0 0 4px; }}
  h2 {{ font-size:16px; margin:28px 0 12px; color:var(--accent); letter-spacing:.3px; }}
  .muted {{ color:var(--muted); font-size:12.5px; }}
  .hero {{ background:rgba(30,107,255,.10); border:1px solid rgba(30,107,255,.35); border-radius:14px; padding:20px 24px; }}
  .score {{ font-size:44px; font-weight:700; }}
  .grade {{ display:inline-block; margin-left:10px; padding:4px 12px; border-radius:999px;
            background:rgba(30,107,255,.18); border:1px solid rgba(30,107,255,.5); font-size:13px; vertical-align:middle; }}
  .grid {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(230px,1fr)); gap:14px; margin-top:14px; }}
  .card {{ background:rgba(11,21,48,.85); border:1px solid rgba(30,107,255,.25); border-radius:12px; padding:14px 16px; }}
  .card-title {{ font-size:13px; font-weight:600; display:flex; justify-content:space-between; }}
  .weight {{ color:var(--muted); font-weight:400; }}
  .bar {{ height:6px; border-radius:99px; background:rgba(143,163,200,.18); margin:10px 0 8px; overflow:hidden; }}
  .bar-fill {{ height:100%; border-radius:99px; }}
  .card-value {{ font-size:14px; font-weight:600; margin-bottom:6px; }}
  table {{ width:100%; border-collapse:collapse; font-size:12.5px; }}
  th, td {{ border:1px solid rgba(30,107,255,.22); padding:7px 9px; text-align:left; vertical-align:top; }}
  th {{ background:rgba(30,107,255,.12); }}
  .ok {{ color:#21C97A; white-space:nowrap; }}
  .bad {{ color:#FF4D5E; white-space:nowrap; }}
  ul {{ padding-left:18px; }}
  li {{ margin-bottom:10px; font-size:13px; }}
  .tag {{ font-size:11px; padding:2px 8px; border-radius:999px; border:1px solid rgba(30,107,255,.4); }}
  .tag-alta {{ color:#FF4D5E; border-color:rgba(255,77,94,.5); }}
  .tag-media {{ color:#F2B93B; border-color:rgba(242,185,59,.5); }}
  .tag-baixa {{ color:#21C97A; border-color:rgba(33,201,122,.5); }}
  .benefit {{ color:#8FA3C8; font-size:12px; margin-top:3px; }}
  footer {{ margin-top:34px; color:var(--muted); font-size:11.5px; border-top:1px solid rgba(30,107,255,.2); padding-top:14px; }}
  @media print {{ body {{ background:#fff; color:#111; }} .hero,.card,table th {{ background:#f4f7ff; }} .muted {{ color:#555; }} }}
</style>
</head>
<body>
  <div class='hero'>
    <h1>{AppConstants.ProductName}</h1>
    <div class='muted'>Relatório de avaliação do sistema · {report.GeneratedAt:dd/MM/yyyy HH:mm} · versão {AppConstants.Version}</div>
    <div style='margin-top:14px'><span class='score'>{report.OverallScoreText}</span><span class='grade'>{report.Grade}</span></div>
    <p>{report.Diagnosis}</p>
  </div>

  <h2>Sub-pontuações</h2>
  <div class='grid'>{cards}
  </div>

  <h2>Critérios medidos</h2>
  <table>
    <thead><tr><th>Dimensão</th><th>Critério</th><th>Valor medido</th><th>Penalização</th><th>Conselho</th></tr></thead>
    <tbody>{criteriaRows}
    </tbody>
  </table>

  <h2>Recomendações</h2>
  <ul>{recommendations}
  </ul>

  <h2>Alterações aplicadas por esta aplicação</h2>
  <ul>{applied}
  </ul>
  {hardware}

  <footer>
    Gerado por {AppConstants.ProductName} {AppConstants.Version} · {AppConstants.Publisher}.<br />
    Este relatório não contém telemetria nem dados pessoais; foi gerado localmente no seu computador.
  </footer>
</body>
</html>";
    }
}
