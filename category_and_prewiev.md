# Задача: визуальный браузер выбора символов EPLAN 2.9

## 1. Контекст

Разрабатывается Add-in для **EPLAN Electric P8 2.9** на C#.

В проекте уже существует собственный диалог выбора символа, однако текущая реализация показывает более 800 символов преимущественно как список имён. Это неудобно:

* имя символа часто ничего не говорит пользователю;
* пользователь может не знать имя нужного символа;
* пользователь может знать, как символ должен выглядеть графически;
* пользователь может знать функциональную категорию и группу символа;
* один символ может иметь до 8 графических вариантов.

Необходимо переделать браузер символов так, чтобы пользователь мог:

1. просматривать символы по функциональной классификации EPLAN;
2. видеть графическое представление **всех вариантов одного символа одновременно**;
3. выбирать конкретный вариант;
4. после выбора вернуть:

   * имя библиотеки;
   * номер/имя символа;
   * номер варианта.

Целевой API: **EPLAN API 2.9**.

---

# 2. Ключевой принцип UI

**Карточка каталога представляет `Symbol`, а не `SymbolVariant`.**

Внутри карточки символа preview должен быть разделён на **8 отдельных подокон/ячеек**, соответствующих вариантам:

```text
       A          B          C          D
   ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐
   │        │ │        │ │        │ │        │
   │   A    │ │   B    │ │   C    │ │   D    │
   │ symbol │ │ symbol │ │ symbol │ │ symbol │
   │        │ │        │ │        │ │        │
   └────────┘ └────────┘ └────────┘ └────────┘

       E          F          G          H
   ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐
   │        │ │        │ │        │ │        │
   │   E    │ │   F    │ │   G    │ │   H    │
   │ symbol │ │ symbol │ │ symbol │ │ symbol │
   │        │ │        │ │        │ │        │
   └────────┘ └────────┘ └────────┘ └────────┘

        Имя символа / номер символа
```

Варианты:

```text
0 = A
1 = B
2 = C
3 = D
4 = E
5 = F
6 = G
7 = H
```

Если конкретного варианта нет, его ячейка должна быть визуально обозначена как отсутствующая и не должна быть кликабельной.

**Вариант 16 (contact image) не включать в стандартную сетку A–H.** Его можно поддержать отдельно в будущем.

---

# 3. Общая архитектура

Не использовать имя символа как основной способ навигации.

Система должна быть разделена на:

```text
EPLAN FunctionDefinitionLibrary
              │
              ▼
      дерево классификации
              │
              ├── Trade
              ├── Area
              ├── Category
              ├── Group
              └── Function Definition
                         │
                         ▼
                       Symbol
                         │
              ┌──────────┼──────────┐
              ▼          ▼          ▼
           Variant A  Variant B  ... Variant H
              │          │             │
              └──────────┼─────────────┘
                         ▼
                   DrawingService
                         │
                         ▼
             8 preview-подокон
                         │
                         ▼
                  выбор конкретного
                       варианта
```

---

# 4. Источник классификации

В EPLAN API 2.9 существует класс:

```csharp
Eplan.EplApi.DataModel.FunctionDefinition
```

У `FunctionDefinition` есть:

```csharp
FunctionDefinition.MainGroup
FunctionDefinition.CategoryRegion
FunctionDefinition.CategoryName
FunctionDefinition.GroupName
FunctionDefinition.Name
FunctionDefinition.Description
```

Эти поля использовать для построения дерева:

```text
Trade
└── Area
    └── Category
        └── Group
            └── Function Definition
```

Документация:

https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.FunctionDefinition_members.html

---

# 5. Получение всех Function Definitions

Использовать:

```csharp
project.FunctionDefinitionLibrary.FunctionDefinitions
```

Источник:

https://www.eplan.help/en-US/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.FunctionDefinitionLibrary~FunctionDefinitions.html

Пример:

```csharp
FunctionDefinition[] definitions =
    project.FunctionDefinitionLibrary.FunctionDefinitions;
```

Для каждого определения извлекать:

```csharp
definition.MainGroup;
definition.CategoryRegion;
definition.CategoryName;
definition.GroupName;
definition.Name;
definition.Description;
```

и формировать дерево.

---

# 6. Не строить классификацию по именам символов

Нельзя делать:

```text
символ
символ
символ
...
```

и самостоятельно пытаться определить категорию по имени.

EPLAN уже содержит официальную функциональную классификацию.

Один Function Definition может быть связан с несколькими графическими Symbol.

Поэтому логика должна быть:

```text
Function Definition
        │
        ▼
      Symbol
        │
        ├── A
        ├── B
        ├── C
        ├── D
        ├── E
        ├── F
        ├── G
        └── H
```

---

# 7. Получение символов

Использовать:

```csharp
project.SymbolLibraries
```

Для каждой библиотеки:

```csharp
symbolLibrary.Symbols
```

Объект:

```csharp
Eplan.EplApi.DataModel.MasterData.Symbol
```

Получить все доступные символы.

---

# 8. Symbol является основной единицей каталога

Внутренняя модель каталога должна хранить **один объект на Symbol**, а варианты должны находиться внутри него.

Не делать основной список:

```text
Symbol A / Variant A
Symbol A / Variant B
Symbol A / Variant C
...
```

Должно быть:

```text
Symbol A
   ├── Variant A
   ├── Variant B
   ├── Variant C
   └── ...
```

Пример модели:

```csharp
public sealed class SymbolBrowserItem
{
    public Symbol Symbol { get; init; }

    public string LibraryName { get; init; }

    public string SymbolName { get; init; }

    public string MainGroup { get; init; }

    public string Area { get; init; }

    public string Category { get; init; }

    public string Group { get; init; }

    public string FunctionDefinitionName { get; init; }

    public string FunctionDefinitionDescription { get; init; }

    public SymbolVariant[] Variants { get; init; }
}
```

---

# 9. Получение вариантов

У `Symbol` использовать:

```csharp
symbol.Variants
```

Документация:

https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.Symbol~Variants.html

Каждый элемент:

```csharp
Eplan.EplApi.DataModel.MasterData.SymbolVariant
```

У варианта:

```csharp
variant.SymbolLibraryName
variant.SymbolName
variant.VariantNr
```

Документация:

https://www.eplan.help/en-US/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.SymbolVariant_members.html

---

# 10. Нумерация вариантов

Для стандартных графических вариантов использовать:

```text
VariantNr 0 → A
VariantNr 1 → B
VariantNr 2 → C
VariantNr 3 → D
VariantNr 4 → E
VariantNr 5 → F
VariantNr 6 → G
VariantNr 7 → H
```

В UI обязательно показывать буквенное обозначение:

```text
A   B   C   D
E   F   G   H
```

Преобразование:

```csharp
private static string GetVariantLetter(int variantNr)
{
    return variantNr >= 0 && variantNr <= 7
        ? ((char)('A' + variantNr)).ToString()
        : variantNr.ToString();
}
```

---

# 11. Графический preview через DrawingService

Не создавать PNG вручную и не разбирать `.slk`/другие внутренние файлы библиотек.

Использовать:

```csharp
Eplan.EplApi.HEServices.DrawingService
```

Документация:

https://www.eplan.help/en-US/infoportal/content/api/2.9/Eplan.EplApi.HEServicesu~Eplan.EplApi.HEServices.DrawingService.html

Описание механизма:

https://www.eplan.help/en-US/infoportal/content/api/2.9/HE_Display.html

Основная схема:

```text
SymbolVariant
      ↓
CreateDisplayList(...)
      ↓
DisplayList
      ↓
DrawDisplayList(...)
      ↓
WinForms control
```

---

# 12. Важное изменение: preview должен быть для каждого варианта

Для каждого `Symbol` создать **один preview-контейнер с 8 отдельными дочерними control**.

Например:

```text
SymbolCard
│
├── VariantPreviewCell A
├── VariantPreviewCell B
├── VariantPreviewCell C
├── VariantPreviewCell D
├── VariantPreviewCell E
├── VariantPreviewCell F
├── VariantPreviewCell G
└── VariantPreviewCell H
```

Каждая ячейка:

```text
VariantPreviewCell
    │
    ├── Label "A"
    │
    └── DrawingService preview
```

---

# 13. Рекомендуемая сетка

Для каждой карточки символа использовать:

```text
┌─────────────────────────────────────────────────┐
│                 SymbolName                      │
├──────────────────┬──────────────────────────────┤
│ A                │ B                            │
│                  │                              │
│     GRAPHIC      │      GRAPHIC                 │
│                  │                              │
├──────────────────┼──────────────────────────────┤
│ C                │ D                            │
│                  │                              │
│     GRAPHIC      │      GRAPHIC                 │
│                  │                              │
├──────────────────┼──────────────────────────────┤
│ E                │ F                            │
│                  │                              │
│     GRAPHIC      │      GRAPHIC                 │
│                  │                              │
├──────────────────┼──────────────────────────────┤
│ G                │ H                            │
│                  │                              │
│     GRAPHIC      │      GRAPHIC                 │
│                  │                              │
└──────────────────┴──────────────────────────────┘
```

Либо при достаточной ширине:

```text
A        B        C        D
┌────┐ ┌────┐ ┌────┐ ┌────┐
│    │ │    │ │    │ │    │
└────┘ └────┘ └────┘ └────┘

E        F        G        H
┌────┐ ┌────┐ ┌────┐ ┌────┐
│    │ │    │ │    │ │    │
└────┘ └────┘ └────┘ └────┘
```

При необходимости размер карточки должен быть адаптивным.

---

# 14. Каждая ячейка должна быть самостоятельной

`VariantPreviewCell` должна:

* знать свой `VariantNr`;
* хранить ссылку на `SymbolVariant`;
* отображать букву A–H;
* рисовать соответствующий вариант;
* быть кликабельной;
* сообщать родительскому `SymbolCard`, что выбран конкретный вариант.

Пример события:

```csharp
public event EventHandler<SymbolVariant> VariantSelected;
```

При клике:

```csharp
VariantSelected?.Invoke(this, Variant);
```

---

# 15. Отсутствующие варианты

Если у символа присутствуют только:

```text
A
B
D
```

то:

```text
A → активен
B → активен
C → disabled
D → активен
E → disabled
F → disabled
G → disabled
H → disabled
```

Необходимо сохранять фиксированное положение A–H, чтобы пользователь всегда понимал структуру.

---

# 16. Выбор варианта

Клик по конкретной ячейке должен выбирать именно `SymbolVariant`.

Например:

```text
пользователь нажал B
       ↓
SelectedSymbol = Symbol
SelectedVariant = SymbolVariant(B)
```

При этом выбранная ячейка должна визуально выделяться:

```text
┌────────────────┐
│ B              │
│                │
│    GRAPHIC     │
│                │
└────────────────┘
       ↑
    selected
```

---

# 17. Основной preview

При выборе ячейки можно дополнительно показывать увеличенное изображение выбранного варианта:

```text
┌───────────────────────────────────────────────┐
│ Symbol: Q1                                    │
│ Variant: B                                    │
│                                               │
│                                               │
│                 LARGE PREVIEW                 │
│                                               │
│                                               │
└───────────────────────────────────────────────┘
```

Большой preview также рисовать через `DrawingService`.

---

# 18. DrawingService и производительность

Не вызывать `CreateDisplayList()` внутри каждого `Paint`.

Правильный принцип:

```text
SymbolVariant изменился
        ↓
CreateDisplayList()
        ↓
Invalidate()
        ↓
Paint
        ↓
DrawDisplayList()
```

То есть:

```csharp
private readonly DrawingService _drawingService =
    new DrawingService();

private void SetVariant(SymbolVariant variant)
{
    _currentVariant = variant;

    _drawingService.CreateDisplayList(variant);

    Invalidate();
}

private void OnPaint(PaintEventArgs e)
{
    _drawingService.DrawDisplayList(
        e,
        ClientRectangle);
}
```

Конкретный overload необходимо сверить с DLL EPLAN 2.9.

---

# 19. Кэш preview

При наличии большого количества символов не требуется одновременно генерировать display list для всех 800+ символов.

Предпочтительно:

```text
карточка появилась на экране
        ↓
создать preview для существующих вариантов
        ↓
закэшировать
```

В будущем можно добавить виртуализацию списка.

На первой реализации допустимо сделать простой вариант без сложной виртуализации.

---

# 20. Функциональная классификация

Использовать:

```text
MainGroup
CategoryRegion
CategoryName
GroupName
FunctionDefinition.Name
```

для построения:

```text
Trade
 └── Area
      └── Category
           └── Group
                └── Function Definition
```

Символ должен быть связан с конкретным Function Definition.

Текстовые названия использовать для UI.

Если есть числовой идентификатор:

```text
Category
Group
ID
```

использовать его для связи объектов.

---

# 21. Символы без классификации

Если символ невозможно корректно связать с Function Definition, не удалять его.

Помещать в специальную группу:

```text
Без классификации
```

или:

```text
Other / Unclassified
```

---

# 22. Фильтры

Добавить:

### Библиотека

```text
[ Все библиотеки ▼ ]
```

### Текстовый поиск

Искать по:

```text
LibraryName
SymbolName
FunctionDefinitionName
FunctionDefinitionDescription
Category
Group
```

### Функциональная классификация

TreeView сам является фильтром.

При выборе:

```text
Category
```

показывать все SymbolCard, относящиеся к этой категории.

При выборе:

```text
Group
```

показывать SymbolCard этой группы.

При выборе:

```text
FunctionDefinition
```

показывать только соответствующие символы.

---

# 23. UI

Рекомендуемый интерфейс:

```text
┌─────────────────────────────────────────────────────────────────────┐
│ Выбор символа                                                       │
├──────────────────────┬──────────────────────────────────────────────┤
│ Function Definition  │ Поиск: [____________________________]        │
│                      │                                              │
│ ▼ Trade              │ ┌────────────────────────────┐               │
│   ▼ Area             │ │ Symbol 123                 │               │
│     ▼ Category       │ ├─────────┬─────────┬────────┤               │
│       ▼ Group        │ │ A       │ B       │ C      │               │
│         • Definition │ │ graphic │ graphic │graphic │               │
│         • Definition │ ├─────────┼─────────┼────────┤               │
│                      │ │ D       │ E       │ F      │               │
│                      │ │ graphic │ graphic │graphic │               │
│                      │ ├─────────┼─────────┼────────┤               │
│                      │ │ G       │ H       │        │               │
│                      │ │ graphic │ graphic │        │               │
│                      │ └─────────┴─────────┴────────┘               │
│                      │                                              │
│                      │ ┌────────────────────────────┐               │
│                      │ │ Symbol 456                 │               │
│                      │ │ A...H                      │               │
│                      │ └────────────────────────────┘               │
├──────────────────────┴──────────────────────────────────────────────┤
│ Выбранный символ: IEC / 123 / Variant B                            │
│                                                                     │
│                     ┌─────────────────────┐                         │
│                     │                     │                         │
│                     │   LARGE PREVIEW     │                         │
│                     │                     │                         │
│                     └─────────────────────┘                         │
│                                                                     │
│                           [Выбрать] [Отмена]                        │
└─────────────────────────────────────────────────────────────────────┘
```

---

# 24. Структура классов

Рекомендуемая архитектура:

```text
SymbolBrowserForm
│
├── SymbolBrowserService
│
├── FunctionDefinitionTree
│
├── SymbolBrowserItem
│
├── SymbolCardControl
│     │
│     ├── VariantPreviewCell A
│     ├── VariantPreviewCell B
│     ├── VariantPreviewCell C
│     ├── VariantPreviewCell D
│     ├── VariantPreviewCell E
│     ├── VariantPreviewCell F
│     ├── VariantPreviewCell G
│     └── VariantPreviewCell H
│
└── SymbolPreviewControl
      └── DrawingService
```

---

# 25. SymbolCardControl

`SymbolCardControl` представляет **один Symbol**.

Он должен содержать:

```csharp
public SymbolBrowserItem Item { get; }
```

и восемь:

```csharp
VariantPreviewCell
```

При создании карточки:

```text
Item.Symbol.Variants
        ↓
найти VariantNr 0..7
        ↓
назначить соответствующим Cell
```

---

# 26. VariantPreviewCell

Каждая ячейка:

```csharp
public sealed class VariantPreviewCell : UserControl
{
    public int VariantNr { get; }

    public SymbolVariant Variant { get; private set; }

    public event EventHandler<SymbolVariant> VariantSelected;
}
```

Логика:

```text
Variant = null
    ↓
ячейка disabled

Variant != null
    ↓
создать DrawingService display list
    ↓
показывать графику
    ↓
разрешить Click
```

---

# 27. Получаемые данные после выбора

После нажатия `Выбрать` вернуть:

```csharp
SelectedSymbol.LibraryName
SelectedSymbol.SymbolName
SelectedVariant.VariantNr
```

Например:

```csharp
public sealed class SelectedSymbolResult
{
    public string LibraryName { get; init; }

    public string SymbolName { get; init; }

    public int VariantNr { get; init; }
}
```

Результат:

```text
LibraryName = "IEC_symbol"
SymbolName = "123"
VariantNr = 1
```

где:

```text
1 = Variant B
```

---

# 28. Не использовать XEGActionInsertSymRef

Не строить решение вокруг:

```text
XEGActionInsertSymRef
```

с параметрами `?`.

Это штатное интерактивное действие предназначено для вставки символа.

Нужное поведение:

```text
свой диалог
→ выбор Symbol/Variant
→ возврат результата
```

поэтому использовать:

```text
DataModel
+
FunctionDefinition
+
Symbol
+
SymbolVariant
+
DrawingService
```

---

# 29. Не использовать undocumented UI Automation

Не:

* искать HWND штатного окна EPLAN;
* эмулировать клики;
* использовать Windows UI Automation для штатного Symbol Selection;
* парсить внутренние служебные файлы GUI;
* извлекать встроенные картинки символов из внутренних ресурсов.

Использовать только документированный API 2.9.

---

# 30. Рекомендуемый процесс формирования каталога

При открытии браузера:

```text
1. Получить текущий Project.

2. Получить:
       FunctionDefinitionLibrary

3. Получить:
       FunctionDefinitions

4. Построить дерево:
       MainGroup
       Area
       Category
       Group
       FunctionDefinition

5. Получить:
       Project.SymbolLibraries

6. Для каждой SymbolLibrary:
       получить Symbols

7. Для каждого Symbol:
       получить Variants

8. Определить FunctionDefinition символа.

9. Создать SymbolBrowserItem.

10. Добавить SymbolBrowserItem
    в соответствующий узел дерева.

11. Создать SymbolCardControl.

12. В SymbolCardControl:
       построить 8 VariantPreviewCell.

13. Для каждого VariantNr 0..7:
       если вариант существует:
           показать preview
       иначе:
           disabled cell.

14. При клике по VariantPreviewCell:
       установить SelectedVariant.

15. При клике "Выбрать":
       вернуть LibraryName,
       SymbolName,
       VariantNr.
```

---

# 31. Псевдокод

```csharp
foreach (SymbolLibrary library in project.SymbolLibraries)
{
    foreach (Symbol symbol in library.Symbols)
    {
        FunctionDefinition fd =
            ResolveFunctionDefinition(symbol, project);

        var item = new SymbolBrowserItem
        {
            Symbol = symbol,
            LibraryName = library.Name,
            SymbolName = symbol.Name,
            MainGroup = fd?.MainGroup,
            Area = fd?.CategoryRegion,
            Category = fd?.CategoryName,
            Group = fd?.GroupName,
            FunctionDefinitionName = fd?.Name,
            FunctionDefinitionDescription = fd?.Description,
            Variants = symbol.Variants
        };

        AddItemToFunctionDefinitionTree(item);
    }
}
```

Затем:

```csharp
var card = new SymbolCardControl(item);

foreach (var variant in item.Variants)
{
    if (variant.VariantNr >= 0 &&
        variant.VariantNr <= 7)
    {
        card.SetVariant(
            variant.VariantNr,
            variant);
    }
}
```

---

# 32. Важное требование к preview

**Нельзя показывать только выбранный вариант одного Symbol.**

При отображении SymbolCard пользователь должен сразу видеть:

```text
A B C D
E F G H
```

То есть 8 ячеек являются частью самой карточки Symbol.

Это позволяет пользователю сразу сравнить варианты одного символа:

```text
Symbol X

A        B        C        D
[img]    [img]    [img]    [img]

E        F        G        H
[img]    [img]    [img]    [img]
```

Пользователь нажимает непосредственно на нужный вариант.

---

# 33. Масштабирование графики

Для каждой `VariantPreviewCell`:

* графика должна полностью помещаться в ячейке;
* сохранять пропорции;
* не обрезаться;
* центрироваться;
* масштабироваться при изменении размера;
* использовать DrawingService;
* не сохраняться на диск в виде PNG для основной реализации.

Большой preview выбранного варианта может использовать тот же `SymbolVariant`.

---

# 34. Производительность

Количество символов может быть 800+.

Поэтому:

* не создавать одновременно сотни больших preview;
* не пересоздавать DisplayList в `Paint`;
* использовать кэш;
* при необходимости реализовать виртуализацию списка карточек;
* начинать оптимизацию после корректной первой реализации.

Минимально допустимая оптимизация:

```text
SymbolCard появился на экране
       ↓
создать preview только его вариантов
```

---

# 35. Критерии готовности

Функциональность считается реализованной, если:

* [ ] работает на EPLAN 2.9;
* [ ] используется существующий собственный диалог;
* [ ] символы классифицируются через Function Definition;
* [ ] дерево построено по Trade / Area / Category / Group / Function Definition;
* [ ] пользователь не обязан знать имя символа;
* [ ] Symbol является основной карточкой каталога;
* [ ] каждая карточка содержит 8 подокон A–H;
* [ ] каждый вариант отображается отдельно;
* [ ] отсутствующие варианты отображаются как disabled;
* [ ] графика всех вариантов рисуется через DrawingService;
* [ ] можно кликнуть на конкретный вариант;
* [ ] выбранный вариант визуально выделяется;
* [ ] присутствует большой preview выбранного варианта;
* [ ] возвращаются LibraryName / SymbolName / VariantNr;
* [ ] символы без Function Definition не теряются;
* [ ] не используется UI Automation;
* [ ] не используется undocumented управление штатным окном выбора;
* [ ] не требуется генерация PNG-файлов символов.

---

# 36. Официальные источники EPLAN API 2.9

FunctionDefinition:

https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.FunctionDefinition.html

FunctionDefinition members:

https://www.eplan.help/en-US/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.FunctionDefinition_members.html

FunctionDefinitionLibrary:

https://www.eplan.help/en-US/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.FunctionDefinitionLibrary~FunctionDefinitions.html

Symbol:

https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.Symbol.html

Symbol.Variants:

https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.Symbol~Variants.html

SymbolVariant:

https://www.eplan.help/en-US/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.SymbolVariant_members.html

Symbol properties:

https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.SymbolPropertyList_members.html

DrawingService:

https://www.eplan.help/en-US/infoportal/content/api/2.9/Eplan.EplApi.HEServicesu~Eplan.EplApi.HEServices.DrawingService.html

DrawingService display:

https://www.eplan.help/en-US/infoportal/content/api/2.9/HE_Display.html
