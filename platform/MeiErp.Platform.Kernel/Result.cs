namespace MeiErp.Platform.Kernel;

/// <summary>
/// The outcome of an operation that is allowed to fail for business reasons.
///
/// A supplier with stock on hand refusing deletion is not exceptional - it is
/// the rule working. Exceptions are for bugs and broken infrastructure; this is
/// for the answer "no, and here is why", which the UI can show to a person.
/// </summary>
public readonly record struct Result
{
    private Result(bool ok, string? error, string? code)
    {
        Ok = ok;
        Error = error;
        Code = code;
    }

    public bool Ok { get; }

    /// <summary>Human-readable reason, written for the person on the screen.</summary>
    public string? Error { get; }

    /// <summary>Stable code for callers that need to branch, e.g. "stock.on-hand".</summary>
    public string? Code { get; }

    public bool Failed => !Ok;

    public static Result Success() => new(true, null, null);

    public static Result Fail(string error, string? code = null) => new(false, error, code);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Fail<T>(string error, string? code = null) =>
        Result<T>.Fail(error, code);
}

/// <inheritdoc cref="Result" />
public readonly record struct Result<T>
{
    private Result(bool ok, T? value, string? error, string? code)
    {
        Ok = ok;
        _value = value;
        Error = error;
        Code = code;
    }

    private readonly T? _value;

    public bool Ok { get; }
    public string? Error { get; }
    public string? Code { get; }
    public bool Failed => !Ok;

    /// <summary>
    /// The value. Throws when the result failed - reading a value off a failure
    /// is a bug in the caller, not a business outcome.
    /// </summary>
    public T Value => Ok
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read Value from a failed result: {Error}");

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Fail(string error, string? code = null) =>
        new(false, default, error, code);

    /// <summary>Drops the value, keeping success or failure. For callers that only care whether it worked.</summary>
    public Result AsResult() => Ok ? Result.Success() : Result.Fail(Error!, Code);

    public static implicit operator Result<T>(T value) => Success(value);
}
