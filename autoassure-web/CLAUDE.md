# AutoAssure Web

The web interface of AutoAssure.

Structure of the core application (layout, auth, etc.)

- /src/models: pure models and pure business logic functions.
- /src/repositories: data access layer, API clients.
- /src/services: business logic services.
- /src/components: react views.
- /src/hooks: react hooks.
- /src/ioc: dependency injection using react context

Structure for each feature (test-management is an example):

- /src/features/test-management
  - /services
  - /models
  - /components
  - /repositories
  - /hooks
  - /ioc

Features are completely independent of others. One feature must not import
anything from others. Must ask me you are unclear what feature we are working
on.
