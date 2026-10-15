namespace RoG.Core.Api;

/// <summary>Wird von der HTTP-Pipeline bei jeder Fehlerantwort und jedem Netzwerkfehler geworfen.</summary>
public sealed class ApiProblemException : Exception
{
    public ApiProblemException()
        : this(new ApiProblem { Code = ApiProblem.Codes.Unknown })
    {
    }

    public ApiProblemException(string message)
        : this(new ApiProblem { Code = ApiProblem.Codes.Unknown, Detail = message })
    {
    }

    public ApiProblemException(string message, Exception innerException)
        : this(new ApiProblem { Code = ApiProblem.Codes.Unknown, Detail = message }, innerException)
    {
    }

    public ApiProblemException(ApiProblem problem, Exception? innerException = null)
        : base(problem?.ToString(), innerException)
    {
        ArgumentNullException.ThrowIfNull(problem);
        Problem = problem;
    }

    public ApiProblem Problem { get; }
}
