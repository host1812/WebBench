package main

import (
	"log"

	"github.com/webbench/golang-service/internal/bootstrap"
	"github.com/webbench/golang-service/internal/config"
	"go.uber.org/fx"
)

func main() {
	cfg, err := config.Load("config.yaml")
	if err != nil {
		log.Fatalf("load config: %v", err)
	}

	app := fx.New(
		bootstrap.Module(),
		fx.Supply(cfg),
	)

	app.Run()
}
