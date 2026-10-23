using NetPulse.Domain.Diagnostics;
using NetPulse.Domain.Optimization;

namespace NetPulse.App.ViewModels;

/// <summary>
/// Spanish display text for Domain enums, shared by the row ViewModels that show them.
/// Display-only: the Domain enums themselves stay in English internally, so nothing here
/// touches the business logic or the tests that assert on it.
/// </summary>
internal static class SpanishDisplayNames
{
    public static string Risk(OptimizationRisk risk) => risk switch
    {
        OptimizationRisk.Low => "Bajo",
        OptimizationRisk.Medium => "Medio",
        OptimizationRisk.High => "Alto",
        _ => risk.ToString(),
    };

    public static string Confidence(DiagnosticConfidence confidence) => confidence switch
    {
        DiagnosticConfidence.Confirmed => "Confirmado",
        DiagnosticConfidence.Probable => "Probable",
        DiagnosticConfidence.Hypothesis => "Hipótesis",
        DiagnosticConfidence.ExternalLimitation => "Limitación externa",
        DiagnosticConfidence.Healthy => "Saludable",
        _ => confidence.ToString(),
    };
}
