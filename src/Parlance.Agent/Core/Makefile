SHELL := bash

NPM ?= npm

.DEFAULT_GOAL := help

.PHONY: help install typecheck test build lint format-check deadcode review

help:
	@printf '%s\n' \
		'Agent core targets:' \
		'  make install' \
		'  make typecheck' \
		'  make test' \
		'  make build' \
		'  make lint' \
		'  make format-check' \
		'  make deadcode' \
		'  make review'

install:
	$(NPM) ci

typecheck:
	$(NPM) run typecheck

test:
	$(NPM) test

build:
	$(NPM) run build

lint:
	$(NPM) run lint

format-check:
	$(NPM) run format:check

deadcode:
	$(NPM) run deadcode

review:
	$(NPM) run review
