# Навыки и автоматизация Unity: проверка 2026-10-23

Для Xena предпочтителен официальный Unity Agent Plugin вместе с Unity CLI и
пакетом Pipeline. Это выбор по необходимым нам возможностям, а не измеренный
результат сравнения скорости разных инструментов.

| Инструмент | Польза в проекте | Проверенное состояние |
| --- | --- | --- |
| [Unity Agent Plugin](https://github.com/Unity-Technologies/unity-agent-plugin) | Официальные навыки Unity: CLI, физика, интерфейс, URP, пакеты, навигация, аудио | README заявляет Codex и Unity 6+; в текущих подключениях не установлен |
| [Unity CLI + Pipeline](https://docs.unity.com/en-us/unity-cli/unity-pipeline/unity-pipeline-package) | Команды живому Editor, C#, сборки и тесты; собственный режим MCP | CLI локально найден, Pipeline в проект не добавлен, живое подключение не проверено |
| [CoplayDev Unity MCP](https://github.com/CoplayDev/unity-mcp) | Сцены, объекты, консоль, снимки, Unity Test Framework | Сторонний MIT-проект; заявлены Unity 2021.3–6.x и Python 3.10+; не установлен |

Набор [Unity-Technologies/skills](https://github.com/Unity-Technologies/skills)
публикует те же навыки отдельно. Для Codex его README рекомендует официальный
плагин. В официальном curated-каталоге OpenAI отдельного Unity-навыка на момент
проверки нет. Поиск по доступному каталогу подключаемых приложений Unity-плагин
не вернул; это не отсутствие официального плагина в собственном marketplace Unity.

Особенно полезные навыки для следующих работ: `unity-cli`, `physics-3d-collision`,
`unity-package-management`, `ui-uitk`, `audio-setup-mixers`, `initialize-ai-navigation`,
`validate-urp-render-graph-renderer-feature`. Наличие навыка не доказывает, что
его типовые настройки подходят оригинальной логике Xena или форматам PS1.

На этом ПК обнаружен `%LOCALAPPDATA%/Unity/bin/unity.exe`, 28 111 840 байт,
версия **1.0.0-beta.13**; в PATH он не зарегистрирован. Проверки выполнены
полным путём: `--version` и `status --format json`. Статус вернул
`STATUS_NO_INSTANCES`, поскольку сейчас нет открытого Editor с Pipeline.
Это не ошибка лицензии или провал существующей Windows-сборки.
Результат сохранён в `build/unity-import/unity-cli-status.json`.
`uv` тоже установлен. Codex CLI имеет команды `plugin` и `plugin marketplace`;
официальный marketplace Unity пока не добавлялся. Конфигурация не изменялась.

[Требование Pipeline](https://docs.unity.com/en-us/unity-cli/unity-pipeline/unity-pipeline-package)
— Unity 6.0+, поэтому 6000.3.25f1 соответствует заявленной границе совместимости.
Это ещё не проверка пакета в нашем проекте. CLI отмечен в документации как
экспериментальный. [Официальная инструкция](https://docs.unity.com/en-us/unity-cli/replace-mcp-server-unity-cli)
объясняет, что старый MCP из `com.unity.ai.assistant` устарел; режим `unity mcp`
и сторонние MCP этим не затронуты. Для нашего агента прямые команды CLI доступны
через shell; MCP нужен только если его интерфейс даст дополнительную пользу.

Следующая проверка перед внедрением: подключить Pipeline в отдельном проверочном
проекте, открыть именно его Editor, дождаться ready, выполнить чтение сцены,
снимок и тест, затем повторить проверку сборки. Действующие Node/batchmode и
Windows smoke-проверки сохраняются как независимая проверка конечного Player.
Новые навыки, Pipeline и MCP в рамках этого исследования не устанавливались.
