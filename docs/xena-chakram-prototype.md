# Управляемый чакрам в прототипе

Удерживайте **Q** для прицеливания, нажмите **ЛКМ** для броска. Пока Q удерживается,
мышь меняет направление полёта, камера сопровождает чакрам. Отпустите Q, чтобы
вернуть оружие. После ловли снова доступен обычный бой. На геймпаде: **RB/R1**,
**X/□**, правый стик. Действие Chakram можно переназначить в паузе.

Чувствительность и инверсия камеры общие с обычным обзором. Во время
прицеливания/броска/полёта/ловли перемещение персонажа приостановлено.
Пауза останавливает полёт, R очищает весь цикл. Удержание кнопки атаки не бросает
оружие повторно после ловли. Без клика отпускание Q отменяет прицеливание.

Исходное [руководство игры](https://www.videogamemanual.com/ps1/Xena%20-%20Warrior%20Princess%20%28USA%29.pdf)
задаёт R1+Cross для броска и удержание R1 с направлением для управления.
Захват `capture_action_reference.mjs chakram` и проверка
`verify_action_reference.mjs chakram` сравнили 171 снимок и 32 832 компонента поз:
`64` — бросок на 21 кадр, `65` — ловля на 19 кадров. В исходном SMA ручная модель
чакрама видима с кадра 3 до 15 и исчезает на кадре 16 (индексация с нуля).
Прототип запускает летящий объект на этой границе; точный момент создания
нативного игрового объекта отдельно не проверен.

Летящая модель экспортируется из оригинальных `CHAKRAM.TMD/SMI/STM` через
`node tools/export_chakram_unity.mjs`: два меша, 39 исходных вершин,
46 треугольников, одна палитровая текстура. Все 135 текстурных UV-углов попали
в загруженную область VRAM. Ресурс находится в игнорируемой Git папке
`Assets/Imported/Resources/Chakram`. Первая проба с `Xena.Geometry_32` заменена:
она включает кисть. Первый экспорт отдельной модели не прошёл валидатор единого
корня; структура исправлена. Сборка теперь обязательно импортирует ресурс
и проверяет два меша и 46 треугольников до выпуска EXE.

**Траектория пока является настройкой прототипа.** Скорость наружу 14 м/с,
поворот до 150°/с, автоматический возврат через 2.2 с или при встрече со стеной,
возврат 20 м/с по записанному пути. Масштаб 400 единиц/м предполагается.
Это не порт исходного алгоритма чакрама, рикошетов, усилений или переключателей.
Непрерывный SphereCast радиуса 0.12 проверяет контакты; стена отсекает цели за ней.
Учебный манекен получает временный урон 35 один раз за бросок, на возврате урона нет.

Windows-проверка: `node tools/check_chakram_player.mjs`, отчёт
`build/unity-import/chakram-player-verification.json`, снимок `chakram-flight.png`.
Проверяются фактический ввод, модель и клипы, управление мышью, камера,
попадание/препятствие, пауза, отмена, возврат, сброс, геймпад и удержание атаки.

## Flight presentation correction (2026-10-24)

The standalone mesh lies in local XZ (0.29 x 0.01 x 0.285 metres). The previous
world-Y Euler spin of a 65-degree tilted mesh changed the disc normal every tick.
Flight now composes a travel-aligned frame with a local-Y spin at 900 degrees/sec.
The normal remains perpendicular to velocity, including steering and return.

A point on the 0.145-metre rim leaves a widening, fading 0.16-second trail.
Trail samples use the simulation clock, freeze during pause, and clear on catch,
reset and the next launch. A Resources material retains its transparent URP shader
in Windows builds. This is a new presentation effect; flight tuning remains provisional.

## Close chase view and impact feedback (2026-10-24)

The outgoing camera is 0.78m behind and 0.28m above the original disc, looking
0.45m ahead. A 35-degree vertical FOV at16:9 (constant horizontal FOV on ultrawide)
places the real mesh at about32% screen width. Near clipping is0.02m; the close
view uses the actual flight direction and has no delayed positional focus.
Sphere casting still protects the camera from solid obstacles. Automatic return
restores the player's original lens/view, avoiding a180-degree close-camera swing
into Xena or the wall on impact. Steering angles remain separate from this lens.

A swept solid contact emits18 short-lived yellow/orange sparks toward the open
side of the surface, reverses heading, and starts a0.12-second initial24m/s rebound
along the outgoing breadcrumbs, followed by the existing20m/s return. This is a
straight return, not an audited original oblique-reflection algorithm. Practice
target damage/pass-through remains as before. Particles manually share the game
clock, freeze onpause, expire after catch and clear onreset. The built-in Particle
System module1.0.0 is a declared dependency of the local asset package.

Windows checks measure projected original mesh vertices (0.319383width), look
direction, restored lens, single burst, actual reverse displacement, wall occlusion,
particle lifetime/position during pause, expiry and reset. Captures:
`build/unity-import/chakram-close-flight.png`, `chakram-impact-sparks.png`.

The later user-requested visual-mode change supersedes the earlier world-space
rim trail: the current Remaster effect is expanding translucent arcs in the disc's
local plane, as described in [visual-modes.md](visual-modes.md). Original hides
this extra halo. With the additional mode-switch test, measured width is0.313356
(slightly different spin phase). Both use the same original0.29m opaque disc.
