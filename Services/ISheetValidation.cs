namespace ElyProxy.Services;

/// <summary>
/// Implemented by sheet content that can block the dialog's primary button
/// (inline validation instead of validating after submit).
/// </summary>
public interface ISheetValidation
{
    bool IsValid { get; }
}
