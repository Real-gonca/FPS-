using Nexus.Domain.Optimization;

namespace Nexus.Application.Profiles;

/// <summary>
/// Perfis de serviços (spec §4.6). Cada alteração referencia um serviço
/// Windows real (nome curto do WMI) e tem justificação explicita —
/// a UI mostra esta lista ANTES de aplicar (preview de impacto).
///
/// Nota de honestidade (spec §0.2): "desativar serviços" não é garantia de
/// ganho de performance — o efeito depende do uso. As justificações dizem o
/// que cada serviço faz e para quem a alteração é adequada.
/// </summary>
public static class ServiceProfileCatalog
{
    public static IReadOnlyList<ServiceProfile> Default => new List<ServiceProfile>
    {
        new(
            key: "gaming",
            name: "Perfil Gaming (jogos e emuladores)",
            description:
                "Desativa serviços de pré-carregamento, partilha de media e pesquisa de ficheiros — " +
                "ciclos de disco/CPU em segundo plano que competem com jogos. Adequado se não imprimir, " +
                "não usar a pesquisa do Windows nem partilhar media com outros dispositivos.",
            risk: RiskLevel.Low,
            changes: new List<ServiceProfileChange>
            {
                new("SysMain", "Superfetch (SysMain)", "disabled",
                    "Pré-carrega programas frequentes; em SSD o ganho é mínimo e é frequentemente desativado para gaming. A app afetada só arranca um pouco mais devagar quando necessário."),
                new("WSearch", "Pesquisa do Windows (WSearch)", "disabled",
                    "Indexa ficheiros para a pesquisa do Explorer — consome disco/CPU em contínuo. Desative apenas se não usar a pesquisa do Windows; a pesquisa em tempo real volta a estar disponível, mas mais lenta."),
                new("WMPNetworkSvc", "Partilha de Rede do Media Player", "disabled",
                    "Partilha a biblioteca do Media Player com outros dispositivos da rede. Sem utilidade se não o fazer."),
                new("DiagTrack", "Experiências de Utilizador Ligado e Telemetria", "disabled",
                    "Envia dados de diagnóstico/telemetria (CEIP). Reduz ciclos em segundo plano e recolha de dados."),
                new("Spooler", "Agente de Impressão (Spooler)", "disabled",
                    "Gerencia a fila de impressão. Desative apenas se não usar impressoras locais/redes; a impressão deixará de funcionar até reverter."),
            }),

        new(
            key: "privacy",
            name: "Perfil Privacidade (menos serviços que enviam dados)",
            description:
                "Desativa serviços associados a telemetria, localização e ecossistema Xbox/loja — " +
                "cada um documentado. Adequado a quem quer minimizar serviços de ligação em segundo plano.",
            risk: RiskLevel.Low,
            changes: new List<ServiceProfileChange>
            {
                new("DiagTrack", "Experiências de Utilizador Ligado e Telemetria", "disabled",
                    "Coleta e envia dados de telemetria/diagnóstico (CEIP). A desativação é a ação de privacidade com maior impacto nos serviços."),
                new("lfsvc", "Serviço de Localização (lfsvc)", "disabled",
                    "Fornece localização a apps e serviços do Windows. Desativar a apps a capacidade de obter localização via este serviço."),
                new("MapsBroker", "Gestor de Mapas Descarregados", "disabled",
                    "Gere mapas offline do app Mapas do Windows. Sem utilidade se não usar o app Mapas offline."),
                new("XblAuthManager", "Gestor de Autenticação do Xbox Live", "disabled",
                    "Autentica a conta Xbox em jogos/apps. Desative apenas se não usar Xbox/loja com conta Xbox; apps Xbox deixarão de iniciar sessão."),
            }),
    };

    public static ServiceProfile? Find(string key) =>
        Default.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
