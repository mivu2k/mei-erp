namespace MeiErp.Platform.Kernel;

/// <summary>
/// Something a scanned code turned out to be, and where to go to see it.
/// </summary>
/// <param name="Title">What the record is called - the number on the sticker, usually.</param>
/// <param name="Subtitle">Enough context to tell two hits apart: the party, the device, the status.</param>
/// <param name="Url">Where to send the person. A record's own page where there is one.</param>
/// <param name="ModuleKey">The module that owns it, used to group the list and to gate it.</param>
/// <param name="Permission">
/// The permission the target page itself demands. A hit nobody may open is worse
/// than no hit: it navigates to a refusal, or worse, leaks that the record exists.
/// </param>
/// <param name="Icon">MudBlazor icon name, resolved by the scan screen.</param>
public sealed record ScanHit(
    string Title,
    string? Subtitle,
    string Url,
    string ModuleKey,
    string? Permission = null,
    string? Icon = null);

/// <summary>
/// How a module answers "what is this barcode?".
///
/// There is one scan screen for the whole suite, because the person holding the
/// scanner does not know - and should not have to know - which module owns the
/// sticker in front of them. The screen depends on this contract only, so the
/// platform still references no module: a module contributes a resolver, and a
/// module that is not installed simply contributes nothing.
///
/// A resolver returns a list rather than a single hit because one code can
/// plausibly mean something in two places - a supplier's document number
/// reused as our own reference, the same serial on a job and on a stock unit.
/// Showing both is honest; silently picking one is how somebody ends up
/// looking at the wrong record and believing it is the right one.
/// </summary>
public interface IScanResolver
{
    /// <summary>The module this resolver answers for, matching its <see cref="ModuleDescriptor.Key"/>.</summary>
    string ModuleKey { get; }

    /// <summary>
    /// Everything in this module that the code identifies. Empty when nothing
    /// matches - that is the normal answer, not a failure.
    /// </summary>
    Task<IReadOnlyList<ScanHit>> ResolveAsync(string code, CancellationToken ct = default);
}
