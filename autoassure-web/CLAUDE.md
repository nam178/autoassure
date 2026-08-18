# AutoAssure Web

The web interface of AutoAssure.

Structure of the core application (layout, auth, etc.)

- /src:
  - /models: pure models and pure business logic functions.
  - /repositories: data access layer, API clients.
  - /services: business logic services.
  - /components: react views.
  - /hooks: react hooks.
  - /ioc: dependency injection using react context

Structure for each feature (test-management is an example):

- /src/features/test-management:
  - /services
  - /models
  - /components
  - /repositories
  - /hooks
  - /ioc

Features are completely independent of others. One feature must not import
anything from others. Must ask me you are unclear what feature we are working
on.

# Workflow

- When you finish your coding task, always run compile, lint and optionally (ask
  me) whenever to run tests. Fix all errors.
