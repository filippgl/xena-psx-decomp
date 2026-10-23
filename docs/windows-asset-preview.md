# Xena: Windows-просмотрщик оригинальной модели

Запуск из папки игры: `Preview-Xena-Unity.cmd`. Либо запустите
`decomp/build/unity-preview/XenaAssetPreview.exe`. Unity Editor и Hub для
просмотра не нужны. В архиве запускайте `XenaAssetPreview/XenaAssetPreview.exe`,
сохранив рядом всю папку данных и библиотеки.

В окне доступны Previous/Next (81 клип), Play/Pause, ползунок кадра, поворот
модели и переключатель Upscale 4x. Используются оригинальные геометрия и анимации;
4× текстуры подготовлены отдельно. Здесь пока нет перемещения, боя и игрового
уровня. Первая локация доступна в отдельной сцене Unity и браузерном просмотре.

Сборка Unity 6000.3.25f1, Windows x64. Проверена настоящим запуском на
RTX 4070 SUPER: 33 Renderer, 81 клип, оригинальные и 4× текстуры дают разные
кадры. Отчёты `build-verification.json` и `player-verification.json` находятся
в `build/unity-import`; там же сохранены снимки `player-original.png` и
`player-upscale.png`. Это проверка ресурсов, а не проходимости игры.

Повторить из `decomp/` при закрытом Editor:

```powershell
node tools/run_unity_import.mjs --editor "../Unity63/Editor/Unity.exe" --build
node tools/check_unity_player.mjs
```
