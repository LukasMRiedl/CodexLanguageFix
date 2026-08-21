namespace CodexLanguageFix.Infrastructure;

/// <summary>
/// Ausgabeprotokolle, die für einen Luna-Benchmark isoliert werden können.
/// Der produktive Standard bleibt <see cref="FullText"/>.
/// </summary>
internal enum LunaOutputProtocol
{
    FullText,
    Segments,
    SpanEdits
}
