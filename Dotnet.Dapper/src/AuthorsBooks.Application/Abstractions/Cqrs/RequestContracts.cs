namespace AuthorsBooks.Application.Abstractions.Cqrs;

public interface IRequest<out TResult>;

public interface ICommand<out TResult> : IRequest<TResult>;

public interface IQuery<out TResult> : IRequest<TResult>;

public interface IRequestHandler<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> Handle(TRequest request, CancellationToken cancellationToken);
}

public delegate Task<TResult> RequestHandlerDelegate<TResult>();

public interface IRequestBehavior<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> Handle(
        TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResult> next);
}

public interface IRequestDispatcher
{
    Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
}
