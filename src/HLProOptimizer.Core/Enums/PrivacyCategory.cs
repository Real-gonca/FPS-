namespace HLProOptimizer.Core.Enums;

/// <summary>Grupos exibidos na tela de Privacidade.</summary>
public enum PrivacyCategory
{
    /// <summary>Telemetria do Windows (DiagTrack, WAP Push, WER, Feedback).</summary>
    Telemetry = 0,

    /// <summary>Rastreamento (Advertising ID, Timeline, Cortana, histórico).</summary>
    Tracking = 1,

    /// <summary>Aplicativos executando em segundo plano.</summary>
    BackgroundApps = 2,

    /// <summary>Permissões (localização, câmera, microfone, notificações).</summary>
    Permissions = 3
}
