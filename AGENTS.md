# Project Rules & Instructions

## Codebase Memory & Indexing Workflow
- **Immediate Indexing**: При начале работы с проектом сразу проверять статус индекса (`list_projects` / `index_status`) и выполнять индексацию через `index_repository`.
- **Token Economy (Экономия токенов)**:
  - Не выполнять сплошной поиск (массивный grep/find по всем директориям).
  - Сначала обращаться к графу знаний кодовой базы (`codebase-memory`):
    - `get_architecture` — обзор архитектуры, пакетов, точек входа
    - `trace_path` — цепочки вызовов функций (inbound/outbound)
    - `search_graph` — быстрый поиск узлов/функций по шаблону имени
    - `query_graph` — связи и Cypher-запросы
    - `detect_changes` — влияние незакоммиченных изменений
    - `get_code_snippet` — чтение только нужных фрагментов кода
  - Использовать данные памяти о коде (хранилище графа / папку `.codebase-memory`), опираясь на граф при анализе и модификации проекта.
