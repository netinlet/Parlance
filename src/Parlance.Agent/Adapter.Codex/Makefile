SHELL := bash

NPM ?= npm

.DEFAULT_GOAL := help

.PHONY: help install typecheck test build

help:
	@printf '%s\n' \
		'Codex adapter targets:' \
		'  make install' \
		'  make typecheck' \
		'  make test' \
		'  make build'

install:
	$(NPM) ci

typecheck:
	$(NPM) run typecheck

test:
	$(NPM) test

build:
	$(NPM) run build
