namespace Chummer.Contracts.Product;

public sealed record DesktopHorizonRouteOption(
    string Id,
    string Label,
    string RelativeHref,
    string Summary);

public sealed record DesktopHorizonNativeAction(
    string Id,
    string Label);

public sealed record DesktopHorizonWorkbenchEntry(
    string Id,
    string Title,
    string Summary,
    DesktopHorizonRouteOption PrimaryAction,
    DesktopHorizonRouteOption? SecondaryAction = null,
    DesktopHorizonRouteOption? TertiaryAction = null,
    IReadOnlyList<DesktopHorizonNativeAction>? NativeActions = null);
