package bootstrap

import (
	"github.com/webbench/golang-service/internal/application"
	"github.com/webbench/golang-service/internal/infrastructure/postgres"
	httpapi "github.com/webbench/golang-service/internal/interfaces/http"
	"go.uber.org/fx"
)

func Module() fx.Option {
	return fx.Options(
		fx.Provide(
			fx.Annotate(
				application.NewSystemClock,
				fx.As(new(application.Clock)),
			),
			application.NewAuthorCommandHandler,
			application.NewAuthorQueryHandler,
			application.NewBookCommandHandler,
			application.NewBookQueryHandler,
			application.NewHealthQueryHandler,
			fx.Annotate(
				postgres.NewPool,
				fx.As(new(postgres.DB)),
				fx.As(new(application.DatabasePinger)),
			),
			fx.Annotate(
				postgres.NewAuthorRepository,
				fx.As(new(application.AuthorCommandStore)),
				fx.As(new(application.AuthorQueryStore)),
			),
			fx.Annotate(
				postgres.NewBookRepository,
				fx.As(new(application.BookCommandStore)),
				fx.As(new(application.BookQueryStore)),
			),
			httpapi.NewRouter,
			httpapi.NewHTTPServer,
		),
		fx.Invoke(httpapi.RegisterServerLifecycle),
	)
}
