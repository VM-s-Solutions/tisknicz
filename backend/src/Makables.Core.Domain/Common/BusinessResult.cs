namespace Makables.Core.Domain.Common;

/// <summary>
/// Result of an operation that can succeed or fail with an expected
/// <see cref="Error"/>. Reserved for happy-path-or-known-failure flows;
/// truly unexpected failures still throw. Per ADR 0002 and patterns §A.4.
/// </summary>
public class BusinessResult
{
    public bool IsSuccess { get; }
    public Error? Error { get; }

    protected BusinessResult(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
            throw new InvalidOperationException("A successful result cannot carry an Error.");
        if (!isSuccess && error is null)
            throw new InvalidOperationException("A failed result must carry an Error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static BusinessResult Success() => new(true, null);

    public static BusinessResult Failure(Error error) => new(false, error);

    public static BusinessResult<T> Success<T>(T value) => new(true, null, value);

    public static BusinessResult<T> Failure<T>(Error error) => new(false, error, default);

    public static BusinessResult ValidationFailure(IReadOnlyList<ValidationDetail> details) =>
        new(false, Common.Error.Validation(details));

    public static BusinessResult<T> ValidationFailure<T>(IReadOnlyList<ValidationDetail> details) =>
        new(false, Common.Error.Validation(details), default);
}

/// <summary>
/// <see cref="BusinessResult"/> carrying a value on success.
/// </summary>
public sealed class BusinessResult<T> : BusinessResult
{
    public T? Value { get; }

    internal BusinessResult(bool isSuccess, Error? error, T? value)
        : base(isSuccess, error)
    {
        Value = value;
    }
}
