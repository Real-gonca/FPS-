namespace HLOptimizerPro.Core.Models;

/// <summary>Nível de saúde usado por métricas, recomendações e cartões de estado.</summary>
public enum HealthLevel
{
    Otimo = 0,      // verde  — dentro dos valores recomendados
    Atencao = 1,    // amarelo — a degradar
    Critico = 2,    // vermelho — requer ação
    Desconhecido = 3,
}

/// <summary>Prioridade de uma recomendação.</summary>
public enum RecommendationPriority
{
    Alta = 0,
    Media = 1,
    Baixa = 2,
}

/// <summary>Categoria de uma recomendação (agrupa na tela Recomendações).</summary>
public enum RecommendationCategory
{
    Desempenho,
    Memoria,
    Rede,
    Armazenamento,
    Seguranca,
    Inicializacao,
    Graficos,
    Sistema,
}

/// <summary>Tipo de licença da conta.</summary>
public enum LicenseTier
{
    Free = 0,
    Premium = 1,
}

/// <summary>Cor de uma métrica para ligação direta ao design system (brush por índice).</summary>
public enum MetricAccent
{
    Azul = 0,
    Verde = 1,
    Amarelo = 2,
    Vermelho = 3,
    Roxo = 4,
}

/// <summary>Resultado da deteção de uma app/jogo instalado.</summary>
public enum AppDetectionState
{
    Desconhecido = 0,
    NaoInstalado = 1,
    Instalado = 2,
    Otimizado = 3,
}

/// <summary>Perfil de tweaks aplicável pelo módulo Tweaks.</summary>
public enum TweakProfile
{
    Competitivo = 0,
    Streaming = 1,
    Balanceado = 2,
    Extremo = 3,
}

/// <summary>Estado do Game Mode.</summary>
public enum GameModeState
{
    Desativado = 0,
    AguardandoJogo = 1,
    AguardandoProcesso = 2,
    Ativo = 3,
    ARestaurar = 4,
}

/// <summary>Aba de Avaliação / Otimizações seguindo as categorias pedidas no briefing.</summary>
public enum OptimizationCategory
{
    Windows = 0,
    Sistema = 1,
    Privacidade = 2,
    UnidadeDisco = 3,
    WindowsUpdate = 4,
    Toque = 5,
    Aplicativos = 6,
}

/// <summary>Área das Otimizações apresentada em cartões (3.5).</summary>
public enum OptimizationArea
{
    DesempenhoSistema = 0,
    Privacidade = 1,
    UnidadeDisco = 2,
    WindowsUpdate = 3,
    Toque = 4,
    Aplicativos = 5,
}
