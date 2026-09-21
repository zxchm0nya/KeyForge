# Antigravity Rules

## Codebase Memory & Indexing
- **Immediate Indexing**: При открытии и начале работы с проектом сразу индексировать проект с помощью `index_repository`.
- **Token Efficiency**: Всегда экономить токены контекста — обращаться к памяти и графу кодовой базы (`codebase-memory`), используя `get_architecture`, `trace_path`, `search_graph`, `query_graph`, `get_code_snippet` вместо "слепого" чтения или поиска по всем файлам проекта. Работать строго на основе сохраненной памяти о коде.
