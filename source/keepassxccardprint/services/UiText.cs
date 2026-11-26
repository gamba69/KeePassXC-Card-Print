using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint.Services;

// Translates UI chrome only. Database records, passwords, template contents and
// field-mapping captions are NOT modified by the language switch.
public static class UiText
{
    public static string Language { get; set; } = "auto";

    private static readonly IReadOnlyDictionary<string, (string Uk, string En)> Lines =
        new Dictionary<string, (string Uk, string En)>(StringComparer.Ordinal)
        {
            ["Перетаскивайте для изменения высоты панелей"] = ("Перетягуйте для зміни висоти панелей", "Drag to resize the panels vertically"),
            ["Размещать панель сверху при достаточной ширине"] = ("Розміщувати панель зверху за достатньої ширини", "Place toolbar at the top when space permits"),
            ["Закрепить панель вертикально слева"] = ("Закріпити панель вертикально ліворуч", "Pin toolbar vertically on the left"),
            ["Статус печати"] = ("Стан друку", "Print status"),
            ["Считать напечатанной"] = ("Позначити як надруковану", "Mark as printed"),
            ["Считать не напечатанной"] = ("Позначити як ненадруковану", "Mark as not printed"),
            ["Не отслеживать"] = ("Не відстежувати", "Do not track"),
            ["Правый клик: контроль напечатанного"] = ("Правий клік: керування станом друку", "Right-click: manage print status"),
            ["Отметить для печати"] = ("Позначити для друку", "Select for printing"),
            ["Выбрать файл базы KeePassXC"] = ("Вибрати файл бази KeePassXC", "Choose KeePassXC database file"),
            ["Выбрать ключевой файл"] = ("Вибрати ключовий файл", "Choose key file"),
            ["! — проверьте параметры поля"] = ("! — перевірте параметри поля", "! — check field settings"),
            ["Увеличить"] = ("Збільшити", "Increase"),
            ["Уменьшить"] = ("Зменшити", "Decrease"),
            ["Логин — телефон"] = ("Логін — телефон", "Phone login"),
            ["Форматировать номер"] = ("Форматувати номер", "Format phone number"),
            ["Только отображение, без изменения данных базы. Поддерживаются явные номера +380 и +1."] = ("Лише відображення, без зміни даних бази. Підтримуються номери +380 і +1.", "Display only; database data is unchanged. Supports +380 and +1 numbers."),
            ["Шаблон"] = ("Шаблон", "Template"),
            ["Выберите шаблон и нажмите «Загрузить»"] = ("Виберіть шаблон і натисніть «Завантажити»", "Select a template and click Load"),
            ["Загрузить выбранный шаблон целиком: обе стороны"] = ("Завантажити вибраний шаблон повністю: обидва боки", "Load the selected template for both sides"),
            ["Загрузить"] = ("Завантажити", "Load"),
            ["Название своего шаблона"] = ("Назва власного шаблону", "Personal template name"),
            ["Создать независимую копию шаблона с оформлением обеих сторон"] = ("Створити незалежну копію шаблону з оформленням обох боків", "Create an independent template copy for both sides"),
            ["Создать мой шаблон"] = ("Створити власний шаблон", "Create my template"),
            ["Сохранить текущие настройки в свой шаблон"] = ("Зберегти поточні параметри у власному шаблоні", "Save current settings to your template"),
            ["Сохранить изменения"] = ("Зберегти зміни", "Save changes"),
            ["Переключение раздела не загружает шаблон и не сбрасывает изменения"] = ("Перемикання розділу не завантажує шаблон і не скидає змін", "Switching sections does not reload the template or discard edits"),
            ["Пустой оборот"] = ("Порожній зворот", "Empty reverse"),
            ["Оформление / пустой / другие режимы, которые доступны в шаблоне"] = ("Оформлення порожнього звороту та інші доступні в шаблоні режими", "Blank reverse design and other template options"),
            ["Шапка"] = ("Шапка", "Header"),
            ["Подвал"] = ("Колонтитул", "Footer"),
            ["Показывать шапку на пустом обороте"] = ("Показувати шапку на порожньому звороті", "Show header on an empty reverse"),
            ["Показывать подвал на пустом обороте"] = ("Показувати колонтитул на порожньому звороті", "Show footer on an empty reverse"),
            ["Перетаскивайте для изменения ширины панелей"] = ("Перетягуйте, щоб змінити ширину панелей", "Drag to resize panels"),
            ["Предпросмотр"] = ("Попередній перегляд", "Preview"),
            ["Номер текущей карточки или листа и общее количество"] = ("Номер поточної картки чи аркуша й загальна кількість", "Current card or sheet number and total"),
            ["Текущий масштаб предпросмотра. Колесо мыши — масштабирование от курсора"] = ("Поточний масштаб перегляду. Коліщатко миші — масштабування від курсора", "Current preview zoom. Mouse wheel zooms around the pointer"),
            ["Предыдущая карточка или лист"] = ("Попередня картка чи аркуш", "Previous card or sheet"),
            ["Следующая карточка или лист"] = ("Наступна картка чи аркуш", "Next card or sheet"),
            ["Переключить: карточка / полный лист"] = ("Перемкнути: картка / повний аркуш", "Toggle card / full sheet"),
            ["Лицевая сторона — выделена, когда активна"] = ("Лицьовий бік — виділений, коли активний", "Front side — highlighted when active"),
            ["Оборотная сторона — выделена, когда активна"] = ("Зворотний бік — виділений, коли активний", "Reverse side — highlighted when active"),
            ["Вписать лист или карточку в окно (Ctrl+0)"] = ("Вмістити аркуш чи картку у вікно (Ctrl+0)", "Fit sheet or card to window (Ctrl+0)"),
            ["Вернуть масштаб 100%, сохранив точку просмотра (Ctrl+1)"] = ("Повернути масштаб 100%, зберігши точку перегляду (Ctrl+1)", "Reset to 100% while preserving view position (Ctrl+1)"),
            ["Настройки листа, анимации, ч/б и масштаба"] = ("Налаштування аркуша, анімації, ч/б і масштабу", "Sheet, animation, monochrome and zoom settings"),
            ["Предпросмотр и лист"] = ("Перегляд і аркуш", "Preview and sheet"),
            ["Показывать полный лист"] = ("Показувати весь аркуш", "Show full sheet"),
            ["Бумага"] = ("Папір", "Paper"),
            ["Альбомная ориентация"] = ("Альбомна орієнтація", "Landscape orientation"),
            ["Переворот по короткому краю"] = ("Перевертання за коротким краєм", "Flip on short edge"),
            ["Первый слот"] = ("Перша комірка", "First slot"),
            ["Номер первого занятого кармашка на первом листе"] = ("Номер першої зайнятої комірки першого аркуша", "Number of the first occupied slot on the first sheet"),
            ["Шаг колеса"] = ("Крок коліщатка", "Mouse wheel step"),
            ["Изменение масштаба за шаг колеса мыши"] = ("Зміна масштабу за один крок коліщатка миші", "Zoom change per mouse wheel step"),
            ["Чёрно-белый предпросмотр"] = ("Чорно-білий перегляд", "Monochrome preview"),
            ["Чёрно-белое отображение без изменений PDF и печатной геометрии"] = ("Чорно-біле відображення без змін PDF і геометрії друку", "Monochrome display without changing PDF or print geometry"),
            ["Анимация предпросмотра"] = ("Анімація попереднього перегляду", "Preview animation"),
            ["Включить переходы"] = ("Увімкнути переходи", "Enable transitions"),
            ["Длительность"] = ("Тривалість", "Duration"),
            ["Плавная регулировка 50–500 мс (шаг 10 мс)"] = ("Плавне налаштування 50–500 мс (крок 10 мс)", "Smooth adjustment 50–500 ms (10 ms step)"),
            ["После длительного пересчёта"] = ("Після тривалого перерахунку", "After a long recalculation"),
            ["При перелистывании карточек и листов"] = ("Під час гортання карток та аркушів", "When navigating cards and sheets"),
            ["При смене лицевой / оборотной стороны"] = ("Під час зміни лицьового / зворотного боку", "When switching front / reverse side"),
            ["Экспорт выбранных карточек: векторный PDF или растр 600 DPI"] = ("Експортувати вибрані картки: векторний PDF або растр 600 DPI", "Export selected cards: vector PDF or 600 DPI raster"),
            ["Векторный PDF…"] = ("Векторний PDF…", "Vector PDF…"),
            ["Растр 600 DPI (ZIP с PNG)…"] = ("Растр 600 DPI (ZIP з PNG)…", "600 DPI raster (PNG ZIP)…"),
            ["Печать на принтере"] = ("Друк на принтері", "Print to printer"),
            ["История и контроль печати"] = ("Історія та контроль друку", "Print history and tracking"),
            ["Разместить панель сверху / вертикально слева"] = ("Розмістити панель зверху / вертикально ліворуч", "Dock toolbar on top / vertically left"),
            ["Разместить панель сверху"] = ("Розмістити панель зверху", "Dock toolbar on top"),
            ["Разместить панель вертикально слева"] = ("Розмістити панель вертикально ліворуч", "Dock toolbar vertically on the left"),
            ["Открыть базу KeePassXC"] = ("Відкрити базу KeePassXC", "Open KeePassXC database"),
            ["Выбрать файл-ключ"] = ("Вибрати ключовий файл", "Choose key file"),
            ["Другие файлы ключей"] = ("Інші файли ключів", "Other key files"),
            ["Все файлы"] = ("Усі файли", "All files"),
            ["Экспорт листов 600 DPI"] = ("Експорт аркушів 600 DPI", "Export sheets at 600 DPI"),
            ["Экспорт векторного документа PDF"] = ("Експорт векторного документа PDF", "Export vector PDF document"),
            ["Печатный документ сохранён. Секреты в экспортированном файле не зашифрованы."] = ("Друкарський документ збережено. Секрети в експортованому файлі не зашифровані.", "Print document saved. Secrets in the exported file are not encrypted."),
            ["Экспорт отменён."] = ("Експорт скасовано.", "Export cancelled."),
            ["Не удалось экспортировать документ. Проверьте место на диске и права доступа."] = ("Не вдалося експортувати документ. Перевірте вільне місце та права доступу.", "Could not export the document. Check disk space and access rights."),
            ["Дождитесь завершения открытия базы."] = ("Дочекайтеся завершення відкриття бази.", "Wait for the database to finish opening."),
            ["Не удалось переключить хранение. Проверьте права записи и свободное место."] = ("Не вдалося перемкнути сховище. Перевірте права запису й вільне місце.", "Could not switch storage. Check write permissions and available space."),
            ["Сбросить маппинг?"] = ("Скинути зіставлення полів?", "Reset field mapping?"),
            ["Текущие настройки соответствия полей будут заменены значениями по умолчанию."] = ("Поточні налаштування зіставлення полів буде замінено типовими значеннями.", "Current field mapping settings will be replaced by defaults."),
            ["Сбросить"] = ("Скинути", "Reset"),
            ["Печать карточек"] = ("Друк карток", "Print cards"),
            ["Свойства…"] = ("Властивості…", "Properties…"),
            ["Напечатать"] = ("Надрукувати", "Print"),
            ["Тестовый лист"] = ("Тестовий аркуш", "Test sheet"),
            ["После печати подтвердите результат, прежде чем отметить карточки напечатанными."] = ("Після друку підтвердьте результат, перш ніж позначати картки надрукованими.", "Confirm the result before marking cards as printed."),
            ["Отмена"] = ("Скасувати", "Cancel"),
            ["Прервать печать?"] = ("Перервати друк?", "Cancel printing?"),
            ["Некоторые страницы уже могли попасть в очередь принтера. После прерывания проверьте физические листы перед повторной печатью."] = ("Деякі сторінки вже могли потрапити в чергу принтера. Перевірте фізичні аркуші, перш ніж друкувати знову.", "Some pages may already be queued. Inspect the physical sheets before printing again."),
            ["Прервать печать"] = ("Перервати друк", "Cancel print job"),
            ["Принтер"] = ("Принтер", "Printer"),
            ["Способ печати"] = ("Спосіб друку", "Print method"),
            ["Лицевые и оборотные"] = ("Лицьові та зворотні", "Front and reverse sides"),
            ["Только лицевые"] = ("Лише лицьові", "Front only"),
            ["Только оборотные"] = ("Лише зворотні", "Reverse only"),
            ["Стороны и подача"] = ("Сторони та подавання", "Sides and paper feed"),
            ["Лицо X"] = ("Лицьовий бік X", "Front X"),
            ["Лицо Y"] = ("Лицьовий бік Y", "Front Y"),
            ["Оборот X"] = ("Зворот X", "Reverse X"),
            ["Оборот Y"] = ("Зворот Y", "Reverse Y"),
            ["Сбросить смещения"] = ("Скинути зміщення", "Reset offsets"),
            ["Сбросить калибровку?"] = ("Скинути калібрування?", "Reset calibration?"),
            ["Смещения лицевой и оборотной сторон будут установлены в 0 мм."] = ("Зміщення лицьового та зворотного боків буде встановлено на 0 мм.", "Front and reverse offsets will be set to 0 mm."),
            ["Калибровка · смещение, мм"] = ("Калібрування · зміщення, мм", "Calibration · offset, mm"),
            ["Задание может быть частично отправлено принтеру. Проверьте распечатанные листы перед повтором."] = ("Завдання може бути частково надіслане принтеру. Перевірте надруковані аркуші перед повтором.", "The job may have been partially sent to the printer. Check printed sheets before retrying."),
            ["Качество драйвера: см. «Свойства…»"] = ("Якість драйвера: див. «Властивості…»", "Driver quality: see Properties…"),
            ["Автоматический дуплекс: не подтверждён"] = ("Автоматичний дуплекс: не підтверджено", "Automatic duplex: not confirmed"),
            ["уточните в свойствах драйвера"] = ("уточніть у властивостях драйвера", "check driver properties"),
            ["Режимы:"] = ("Режими:", "Modes:"),
            ["HP FastRes 600/1200 — в «Свойствах…»"] = ("HP FastRes 600/1200 — у «Властивостях…»", "HP FastRes 600/1200 — in Properties…"),
            ["Двусторонняя печать: только вручную (HP P1102w)"] = ("Двосторонній друк: лише вручну (HP P1102w)", "Two-sided printing: manual only (HP P1102w)"),
            ["Автодуплекс: заявлен драйвером; проверьте наличие механизма"] = ("Автодуплекс: заявлено драйвером; перевірте наявність механізму", "Auto-duplex: advertised by driver; verify hardware support"),
            ["Принтер не выбран."] = ("Принтер не вибрано.", "No printer selected."),
            ["Ручной переворот каждого листа"] = ("Ручне перевертання кожного аркуша", "Manually flip each sheet"),
            ["Все страницы подряд — без переворота"] = ("Усі сторінки підряд — без перевертання", "All pages in sequence — no flipping"),
            ["Аппаратный дуплекс (принтер переворачивает)"] = ("Апаратний дуплекс (принтер перевертає)", "Hardware duplex (printer flips sheets)"),
            ["Растровая печать"] = ("Растровий друк", "Raster printing"),
            ["Автоматически (растр 600 DPI)"] = ("Автоматично (растр 600 DPI)", "Automatic (600 DPI raster)"),
            ["Без пауз: лицо листа 1, оборот листа 1, лицо листа 2, оборот листа 2… Каждая страница на ОТДЕЛЬНОМ листе бумаги."] = ("Без пауз: лицьовий бік аркуша 1, зворот аркуша 1, лицьовий бік аркуша 2, зворот аркуша 2… Кожна сторінка на ОКРЕМОМУ аркуші.", "No pauses: sheet 1 front, sheet 1 reverse, sheet 2 front, sheet 2 reverse… Each page is printed on a SEPARATE physical sheet."),
            ["Обе стороны одного листа печатает механизм принтера; ручной переворот не требуется."] = ("Обидва боки аркуша друкує механізм принтера; ручне перевертання не потрібне.", "Both sides are printed by the printer hardware; no manual flip is needed."),
            ["По одному листу: лицо → остановка → переворот этого листа → оборот → проверка."] = ("По одному аркушу: лицьовий бік → зупинка → перевертання → зворот → перевірка.", "One sheet at a time: front → pause → flip that sheet → reverse → verify."),
            ["Один проход: все выбранные стороны печатаются подряд на отдельных листах."] = ("Один прохід: усі вибрані боки друкуються підряд на окремих аркушах.", "One pass: all selected sides are printed in sequence on separate sheets."),
            ["Разрешение изображения приложения: 300 / 600 / 1200 DPI. Качество принтера — в «Свойствах…»."] = ("Роздільність зображення: 300 / 600 / 1200 DPI. Якість друку — у «Властивостях…».", "Application image resolution: 300 / 600 / 1200 DPI. Printer quality is configured in Properties…"),
            ["Доступна растровая печать 600 DPI. Векторная печать для этого устройства не подтверждена."] = ("Доступний растровий друк 600 DPI. Векторний друк для цього пристрою не підтверджено.", "600 DPI raster printing is available. Vector printing has not been verified for this device."),
            ["Смещения должны быть числами от −10 до +10 мм."] = ("Зміщення мають бути числами від −10 до +10 мм.", "Offsets must be between −10 and +10 mm."),
            ["Драйвер не заявил поддержку автоматического дуплекса. Выберите ручной."] = ("Драйвер не повідомив про підтримку автоматичного дуплексу. Виберіть ручний режим.", "The driver did not report automatic duplex support. Choose manual mode."),
            ["Для выбранной стороны нет печатных страниц."] = ("Для вибраного боку немає сторінок для друку.", "No printable pages for the selected side."),
            ["Автоматический дуплекс"] = ("Автоматичний дуплекс", "Automatic duplex"),
            ["Убедитесь, что принтер ФИЗИЧЕСКИ переворачивает лист сам. Параметр драйвера не доказывает наличие механизма автопереворота. Печатать обе стороны одним заданием?"] = ("Переконайтеся, що принтер ФІЗИЧНО перевертає аркуш сам. Параметр драйвера не доводить наявності механізму автоматичного перевертання. Друкувати обидва боки одним завданням?", "Make sure the printer PHYSICALLY flips the sheet. A driver setting does not prove automatic duplex hardware is present. Print both sides in one job?"),
            ["Печатать"] = ("Друкувати", "Print"),
            ["Возьмите ТОЛЬКО ЧТО напечатанный лист. Переверните его по проверенной на тестовом листе схеме подачи вашего принтера и вставьте обратно. Не переворачивайте всю стопку."] = ("Візьміть ЩОЙНО надрукований аркуш. Переверніть його за перевіреною на тестовому аркуші схемою подавання та вставте назад. Не перевертайте весь стос.", "Take ONLY the sheet just printed. Flip it following the feed pattern verified with a test sheet, and reinsert it. Do not flip the entire stack."),
            ["Печатать оборот"] = ("Друкувати зворот", "Print reverse"),
            ["Тестовый лист отправлен. Сравните стороны на просвет."] = ("Тестовий аркуш надіслано. Порівняйте боки на просвіт.", "Test sheet submitted. Compare both sides against the light."),
            ["Не подтверждено"] = ("Не підтверджено", "Not confirmed"),
            ["Лист оставлен без подтверждения. Остальные листы не печатались."] = ("Аркуш залишено без підтвердження. Решта аркушів не друкувалися.", "The sheet was left unconfirmed. Remaining sheets were not printed."),
            ["Подтверждено"] = ("Підтверджено", "Confirmed"),
            ["Все листы поштучно напечатаны и проверены."] = ("Усі аркуші надруковано й перевірено поштучно.", "All sheets printed and checked individually."),
            ["Тестовый лист отправлен. Проверьте геометрию и поправьте смещения."] = ("Тестовий аркуш надіслано. Перевірте геометрію та скоригуйте зміщення.", "Test sheet sent. Check alignment and adjust offsets."),
            ["Результат записан в историю и состояния карточек."] = ("Результат записано в історію та стани карток.", "Result saved to history and card statuses."),
            ["Задание не подтверждено; статусы карточек не менялись."] = ("Завдання не підтверджено; стани карток не змінювалися.", "Job not confirmed; card statuses were unchanged."),
            ["Прервано / проверить вручную"] = ("Перервано / перевірити вручну", "Interrupted / check manually"),
            ["Печать прервана. Возможны уже отпечатанные листы, статусы не изменены."] = ("Друк перервано. Деякі аркуші могли бути надруковані; стани не змінено.", "Printing interrupted. Some sheets may have printed; statuses unchanged."),
            ["Ошибка / проверить вручную"] = ("Помилка / перевірити вручну", "Error / check manually"),
            ["Подтверждение печати"] = ("Підтвердження друку", "Print confirmation"),
            ["Всё напечатано"] = ("Усе надруковано", "All printed"),
            ["Сохранить с браком"] = ("Зберегти з браком", "Save with failures"),
            ["Какие физические карточки НЕ получились? Отметьте брак."] = ("Які фізичні картки НЕ вдалися? Позначте брак.", "Which physical cards FAILED? Mark the failed ones."),
            ["История печати"] = ("Історія друку", "Print history"),
            ["Закрыть"] = ("Закрити", "Close"),
            ["Откройте KDBX, чтобы увидеть историю печати."] = ("Відкрийте KDBX, щоб побачити історію друку.", "Open KDBX to see print history."),
            ["Подтверждённых или отправленных заданий пока нет."] = ("Підтверджених або надісланих завдань поки немає.", "There are no confirmed or submitted jobs yet."),
            ["База не открыта"] = ("Базу не відкрито", "No database open"),
            ["База закрыта"] = ("Базу закрито", "Database closed"),
            ["YubiKey не обнаружен"] = ("YubiKey не знайдено", "YubiKey not found"),
            ["Открываю KDBX…"] = ("Відкриваю KDBX…", "Opening KDBX…"),
            ["Журнал печати недоступен; прежние статусы не менялись. Проверьте резервную копию."] = ("Журнал друку недоступний; попередні стани не змінено. Перевірте резервну копію.", "Print ledger unavailable; previous statuses were unchanged. Check the backup."),
            ["Нет разрешения на чтение базы или файла-ключа."] = ("Немає дозволу на читання бази або ключового файлу.", "No permission to read the database or key file."),
            ["Ошибка чтения базы или файла-ключа."] = ("Помилка читання бази або ключового файлу.", "Could not read the database or key file."),
            ["Не удалось открыть базу KeePassXC. Проверьте файл и параметры доступа."] = ("Не вдалося відкрити базу KeePassXC. Перевірте файл і параметри доступу.", "Could not open KeePassXC database. Check the file and access settings."),
            ["Не удалось сохранить настройки."] = ("Не вдалося зберегти налаштування.", "Could not save settings."),
            ["Нет доступа для сохранения настроек."] = ("Немає дозволу на збереження налаштувань.", "No permission to save settings."),
            ["Не удалось сохранить текущие настройки оформления."] = ("Не вдалося зберегти поточні параметри оформлення.", "Could not save current appearance settings."),
            ["Есть несохранённые изменения. Сохраните их или повторно нажмите «Загрузить», чтобы отказаться от них."] = ("Є незбережені зміни. Збережіть їх або ще раз натисніть «Завантажити», щоб відхилити.", "There are unsaved changes. Save them or click Load again to discard them."),
            ["Не удалось открыть шаблон."] = ("Не вдалося відкрити шаблон.", "Could not load the template."),
            ["Нет прав для сохранения шаблона."] = ("Немає прав для збереження шаблону.", "Not allowed to save the template."),
            ["Встроенные шаблоны не изменяются. Сначала создайте свой шаблон."] = ("Вбудовані шаблони не можна змінювати. Спочатку створіть власний шаблон.", "Built-in templates cannot be modified. Create a personal template first."),
            ["Не удалось сохранить изменения в свой шаблон."] = ("Не вдалося зберегти зміни у власному шаблоні.", "Could not save changes to your template."),
            ["Карточка не помещается на выбранном листе без уменьшения. Выберите другой формат."] = ("Картка не вміщується на вибраному аркуші без зменшення. Виберіть інший формат.", "The card does not fit on the selected sheet without scaling. Choose another format."),
            ["Не найдены переносимые шрифты. Проверьте комплектность папки fonts/ в дистрибутиве."] = ("Не знайдено портативних шрифтів. Перевірте папку fonts/ у дистрибутиві.", "Portable fonts were not found. Check the distribution's fonts/ folder."),
            ["Не удалось построить макет. Проверьте размеры бумаги, поля и QR."] = ("Не вдалося побудувати макет. Перевірте розмір паперу, поля та QR.", "Could not generate layout. Check paper size, margins and QR code."),
            ["Не удалось отрисовать карточку или лист."] = ("Не вдалося відмалювати картку чи аркуш.", "Could not render card or sheet."),
            ["Не удалось прочитать настройки поддоменов; проверьте domains.json."] = ("Не вдалося прочитати налаштування піддоменів; перевірте domains.json.", "Could not read subdomain settings; check domains.json."),
            ["Ошибка сохранения списка поддоменов"] = ("Помилка збереження списку піддоменів", "Could not save subdomain list"),
            ["Не удалось открыть свойства драйвера"] = ("Не вдалося відкрити властивості драйвера", "Could not open driver properties"),
            ["Рабочее разрешение драйвера:"] = ("Робоча роздільність драйвера:", "Driver resolution:"),
            ["Автоматически ("] = ("Автоматично (", "Automatic ("),
            ["Векторный документ"] = ("Векторний документ", "Vector document"),
            ["Векторный"] = ("Векторний", "Vector"),
            ["Сначала"] = ("Спочатку", "Try"),
            ["если отправка ещё не началась и векторный режим недоступен — растр 600 DPI."] = ("якщо надсилання ще не почалося і векторний режим недоступний — растр 600 DPI.", "if no pages were submitted and vector mode is unavailable, use 600 DPI raster."),
            ["Драйвер может преобразовать его в растр."] = ("Драйвер може перетворити його на растр.", "The driver may rasterize it."),
            ["-документ передаётся в"] = ("-документ передається до", "-document is sent to"),
            ["Лист"] = ("Аркуш", "Sheet"),
            ["печать лицевой стороны…"] = ("друк лицьового боку…", "printing front…"),
            ["печать оборота…"] = ("друк звороту…", "printing reverse…"),
            ["переворот"] = ("перевертання", "flip"),
            ["Отправка страниц в"] = ("Надсилання сторінок до", "Submitting pages to"),
            ["Ошибка печати:"] = ("Помилка друку:", "Print error:"),
            ["Статусы карточек не изменены."] = ("Стани карток не змінено.", "Card statuses unchanged."),
            ["Если задание могло уйти в"] = ("Якщо завдання могло потрапити до", "If the job might have been sent to"),
            ["не повторяйте печать до проверки листов."] = ("не повторюйте друк до перевірки аркушів.", "do not retry until you inspect the sheets."),
            ["Результат отправлен в очередь"] = ("Результат надіслано в чергу", "Job submitted to queue"),
            ["Брак:"] = ("Брак:", "Failed:"),
            ["Позиция"] = ("Позиція", "Position"),
            ["карточек"] = ("карток", "cards"),
            ["листов"] = ("аркушів", "sheets"),
            ["Изменение языка"] = ("Зміна мови", "Change language"),
            ["Выберите поле и параметры."] = ("Виберіть поле й параметри.", "Select a field and its settings."),
            ["Размер значения"] = ("Розмір значення", "Value size"),
            ["Символов в группе"] = ("Символів у групі", "Characters per group"),
            ["Отступ между группами"] = ("Відступ між групами", "Space between groups"),
            ["Отступ: 0 или 0,1–2,0"] = ("Відступ: 0 або 0,1–2,0", "Spacing: 0 or 0.1–2.0"),
            ["Приоритет"] = ("Пріоритет", "Priority"),
            ["Интервал букв"] = ("Інтервал між літерами", "Letter spacing"),
            ["Коды"] = ("Коди", "Codes"),
            ["Не напечатана"] = ("Не надруковано", "Not printed"),
            ["Напечатана"] = ("Надруковано", "Printed"),
            ["Частично"] = ("Частково", "Partial"),
            ["Слишком большой журнал печати."] = ("Завеликий журнал друку.", "Print ledger is too large."),
            ["Не удалось прочесть журнал печати."] = ("Не вдалося прочитати журнал друку.", "Could not read print ledger."),
            ["Неизвестная версия журнала печати."] = ("Невідома версія журналу друку.", "Unknown print ledger version."),
            ["Шаблон должен находиться в каталоге templates."] = ("Шаблон має бути в каталозі templates.", "Template must be in the templates directory."),
            ["Файл шаблона слишком велик."] = ("Файл шаблону завеликий.", "Template file is too large."),
            ["Неподдерживаемый шаблон оформления."] = ("Непідтримуваний шаблон оформлення.", "Unsupported appearance template."),
            ["Можно сохранять только собственные шаблоны в папке templates."] = ("Можна зберігати лише власні шаблони в папці templates.", "Only personal templates can be saved in templates."),
            ["Имя шаблона: 1–64 символа, буквы, цифры, пробелы, - или _."] = ("Назва шаблону: 1–64 символи, літери, цифри, пробіли, - або _.", "Template name: 1–64 characters, letters, digits, spaces, - or _."),
            ["Шаблон с таким именем уже существует. Укажите другое имя."] = ("Шаблон із такою назвою вже існує. Виберіть іншу назву.", "A template with that name already exists. Choose a different name."),
            ["Пользовательский шаблон не найден."] = ("Користувацький шаблон не знайдено.", "Personal template not found."),
            ["Не выбран принтер или печатаемые страницы."] = ("Не вибрано принтер чи сторінки для друку.", "No printer or printable pages selected."),
            ["Драйвер не предоставил свойства печати."] = ("Драйвер не надав властивостей друку.", "Driver did not provide print settings."),
            ["Не удалось получить параметры драйвера."] = ("Не вдалося отримати параметри драйвера.", "Could not retrieve driver settings."),
            ["Не удаётся получить настройки принтера."] = ("Не вдається отримати налаштування принтера.", "Could not retrieve printer settings."),
            ["Для печати необходим Windows."] = ("Для друку потрібна Windows.", "Printing requires Windows."),
            ["Драйвер вернул неполные параметры печати."] = ("Драйвер повернув неповні параметри друку.", "Driver returned incomplete print settings."),
            ["Не удалось открыть принтер Windows."] = ("Не вдалося відкрити принтер Windows.", "Could not open Windows printer."),
            ["Драйвер не сообщил размеры печатной страницы."] = ("Драйвер не повідомив розміри сторінки для друку.", "Driver did not report printable page dimensions."),
            ["Этот принтер не поддерживает автоматическую двустороннюю печать."] = ("Цей принтер не підтримує автоматичний двосторонній друк.", "This printer does not support automatic duplex printing."),
            ["Windows не приняла задание печати."] = ("Windows не прийняла завдання друку.", "Windows rejected the print job."),
            ["Не удалось начать страницу."] = ("Не вдалося почати сторінку.", "Could not start page."),
            ["Драйвер не принял растровую полосу."] = ("Драйвер відхилив растрову смугу.", "Driver rejected raster band."),
            ["Не удалось завершить страницу."] = ("Не вдалося завершити сторінку.", "Could not finish page."),
            ["Нативный модуль XPS не установлен."] = ("Нативний модуль XPS не встановлено.", "Native XPS component is not installed."),
            ["Все страницы одного задания должны иметь одинаковый формат."] = ("Усі сторінки одного завдання мають мати однаковий формат.", "All pages in one job must use the same paper format."),
            ["Не удалось подготовить векторную страницу XPS."] = ("Не вдалося підготувати векторну сторінку XPS.", "Could not prepare XPS vector page."),
            ["Не загружена библиотека XPS."] = ("Бібліотеку XPS не завантажено.", "XPS library not loaded."),
            ["Несовместимая версия модуля XPS."] = ("Несумісна версія модуля XPS.", "Incompatible XPS module version."),
            ["Проверьте принтер, драйвер, свойства задания и доступность XPS."] = ("Перевірте принтер, драйвер, властивості завдання та доступність XPS.", "Check printer, driver, job properties and XPS availability."),
            ["Файл базы KDBX не найден."] = ("Файл бази KDBX не знайдено.", "KDBX database file not found."),
            ["Файл-ключ не найден."] = ("Ключовий файл не знайдено.", "Key file not found."),
            ["Укажите пароль, файл-ключ или YubiKey."] = ("Укажіть пароль, ключовий файл або YubiKey.", "Provide a password, key file, or YubiKey."),
            ["Проверка файла-ключа…"] = ("Перевірка ключового файлу…", "Checking key file…"),
            ["Ожидание YubiKey; при необходимости коснитесь ключа…"] = ("Очікування YubiKey; за потреби торкніться ключа…", "Waiting for YubiKey; touch the key if prompted…"),
            ["YubiKey Challenge-Response для KDBX 3 пока не поддерживается."] = ("YubiKey Challenge-Response для KDBX 3 поки не підтримується.", "YubiKey Challenge-Response is not yet supported for KDBX 3."),
            ["Не удалось прочитать заголовок KDBX 4 для YubiKey."] = ("Не вдалося прочитати заголовок KDBX 4 для YubiKey.", "Could not read the KDBX 4 header for YubiKey."),
            ["YubiKey недоступен, выбран неверный слот или операция не выполнена."] = ("YubiKey недоступний, вибрано неправильний слот або операцію не виконано.", "YubiKey unavailable, incorrect slot selected, or operation failed."),
            ["Чтение базы KDBX…"] = ("Читання бази KDBX…", "Reading KDBX database…"),
            ["Файл KDBX обрезан, повреждён или имеет неподдерживаемый формат."] = ("Файл KDBX обрізано, пошкоджено або він має непідтримуваний формат.", "The KDBX file is truncated, damaged, or has an unsupported format."),
            ["Библиотека KDBX не смогла прочитать базу. Возможна неподдерживаемая особенность формата."] = ("Бібліотека KDBX не змогла прочитати базу. Можлива непідтримувана особливість формату.", "The KDBX library could not read the database; the format may contain unsupported features."),
            ["Неверный мастер-пароль или другой компонент ключа (файл-ключ / YubiKey). Если данные верны, проверьте целостность KDBX."] = ("Неправильний майстер-пароль або інший компонент ключа (ключовий файл / YubiKey). Якщо дані правильні, перевірте цілісність KDBX.", "Incorrect master password or other key component (key file / YubiKey). If credentials are correct, check the KDBX integrity."),
            ["Ошибка структуры KDBX; возможны повреждение или несовместимость библиотеки чтения."] = ("Помилка структури KDBX; файл може бути пошкоджено або бібліотека читання несумісна.", "KDBX structure error; the file may be damaged or the reader library may be incompatible."),
            ["Не совпала контрольная сумма KDBX. Файл, возможно, повреждён."] = ("Контрольна сума KDBX не збігається. Можливо, файл пошкоджено.", "KDBX checksum mismatch; the file may be damaged."),
            ["Неверная сигнатура KDBX."] = ("Неправильна сигнатура KDBX.", "Invalid KDBX signature."),
            ["YubiKey для KDBX 3 пока не поддерживается. Откройте базу в KeePassXC и обновите до KDBX 4."] = ("YubiKey для KDBX 3 поки не підтримується. Відкрийте базу в KeePassXC та оновіть до KDBX 4.", "YubiKey is not yet supported for KDBX 3. Open the database in KeePassXC and upgrade it to KDBX 4."),
            ["Недопустимая длина поля заголовка."] = ("Неприпустима довжина поля заголовка.", "Invalid header field length."),
            ["В заголовке KDBX 4 не найдены параметры KDF."] = ("У заголовку KDBX 4 не знайдено параметри KDF.", "KDF parameters were not found in the KDBX 4 header."),
            ["Неподдерживаемый VariantDictionary."] = ("Непідтримуваний VariantDictionary.", "Unsupported VariantDictionary."),
            ["Недопустимое имя параметра KDF."] = ("Неприпустима назва параметра KDF.", "Invalid KDF parameter name."),
            ["Недопустимое значение параметра KDF."] = ("Неприпустиме значення параметра KDF.", "Invalid KDF parameter value."),
            ["В параметрах KDF отсутствует соль S."] = ("У параметрах KDF відсутня сіль S.", "KDF parameters are missing salt S."),
            ["Не более 200 служебных поддоменов."] = ("Не більш як 200 службових піддоменів.", "Maximum 200 service subdomains."),
            ["Вводите один DNS-компонент без точек (a-z, 0-9, дефис)."] = ("Введіть один DNS-компонент без крапок (a-z, 0-9, дефіс).", "Enter one DNS label without dots (a-z, 0-9, hyphen)."),
            ["Файл служебных поддоменов слишком велик."] = ("Файл службових піддоменів завеликий.", "Service subdomain file is too large."),
            ["Неизвестный формат списка поддоменов."] = ("Невідомий формат списку піддоменів.", "Unknown service subdomain list format."),
            ["Не удалось прочитать список служебных поддоменов."] = ("Не вдалося прочитати список службових піддоменів.", "Could not read service subdomain list."),
            ["Недопустимая геометрия карточки."] = ("Неприпустима геометрія картки.", "Invalid card geometry."),
            ["Недопустимое число колонок кодов."] = ("Неприпустима кількість колонок кодів.", "Invalid number of code columns."),
            ["Недостаточно места для содержимого карточки."] = ("Недостатньо місця для вмісту картки.", "Not enough space for card contents."),
            ["Поле не помещается на карточке."] = ("Поле не вміщується на картці.", "The field does not fit on the card."),
            ["Заголовок продолжения не помещается."] = ("Заголовок продовження не вміщується.", "Continuation heading does not fit."),
            ["Слишком узкая колонка значений."] = ("Стовпець значень занадто вузький.", "Value column is too narrow."),
            ["Коды восстановления не помещаются по ширине."] = ("Коди відновлення не вміщуються за шириною.", "Recovery codes exceed available width."),
            ["Вектор XPS"] = ("Векторний XPS", "Vector XPS"),
            ["Растр 600 DPI (резерв)"] = ("Растр 600 DPI (резервний режим)", "Raster 600 DPI (fallback)"),
            ["YubiKey не обнаружен. Проверьте подключение USB."] = ("YubiKey не знайдено. Перевірте з’єднання USB.", "YubiKey not detected. Check the USB connection."),
            ["YubiKey вернул ответ неожиданной длины."] = ("YubiKey повернув відповідь неочікуваної довжини.", "YubiKey returned a response of unexpected length."),
        };

    private static readonly IReadOnlyDictionary<string, string> Reverse = BuildReverse();

    private static IReadOnlyDictionary<string, string> BuildReverse()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (source, value) in Lines)
        {
            result.TryAdd(value.Uk, source);
            result.TryAdd(value.En, source);
        }
        return result;
    }

    private static string Resolved(string? language) => Localizer.ResolveLanguage(language ?? Language);

    public static string Tr(string? original, string? language = null)
    {
        if (string.IsNullOrEmpty(original)) return original ?? "";
        string source = Reverse.TryGetValue(original, out var russian) ? russian : original;
        var lang = Resolved(language);
        if (lang == "ru") return source;
        if (Lines.TryGetValue(source, out var value)) return lang == "uk" ? value.Uk : value.En;
        // For interpolated status lines, translate only known UI fragments.
        // Longer fragments precede shorter ones to avoid overlapping matches.
        if (!ContainsCyrillic(source)) return source;
        string translated = source;
        foreach (var (ru, options) in Fragments)
            translated = translated.Replace(ru, lang == "uk" ? options.Uk : options.En,
                StringComparison.Ordinal);
        // Print failure details may contain a known error sentence inside a larger status.
        if (ContainsCyrillic(translated))
            foreach (var (ru, options) in EmbeddedLines)
                translated = translated.Replace(ru, lang == "uk" ? options.Uk : options.En,
                    StringComparison.Ordinal);
        return translated;
    }

    private static bool ContainsCyrillic(string text)
        => text.Any(ch => ch >= '\u0400' && ch <= '\u052F');

    private static readonly (string Ru, (string Uk, string En) Options)[] EmbeddedLines =
        Lines.Where(item => item.Key.Length >= 22 && item.Key.EndsWith(".", StringComparison.Ordinal))
            .OrderByDescending(item => item.Key.Length)
            .Select(item => (item.Key, item.Value)).ToArray();

    private static readonly (string Ru, string Uk, string En)[] FragmentSource =
    [
        ("Прочитано записей: ", "Прочитано записів: ", "Entries read: "),
        (" лист(ов) · ", " аркушів · ", " sheets · "),
        ("Растр ", "Растр ", "Raster "),
        ("Рабочее разрешение драйвера: ", "Робоча роздільність драйвера: ", "Driver resolution: "),
        ("уточните в свойствах драйвера", "уточніть у властивостях драйвера", "check driver properties"),
        (" · Режимы: ", " · Режими: ", " · Modes: "),
        (" · HP FastRes 600/1200 — в «Свойствах…»", " · HP FastRes 600/1200 — у «Властивостях…»", " · HP FastRes 600/1200 — in Properties…"),
        ("Автоматически (", "Автоматично (", "Automatic ("),
        ("Векторный документ ", "Векторний документ ", "Vector document "),
        ("Векторный ", "Векторний ", "Vector "),
        ("Сначала ", "Спочатку ", "First try "),
        ("; если отправка ещё не началась и векторный режим недоступен — растр 600 DPI.", "; якщо надсилання ще не почалося і векторний режим недоступний — растр 600 DPI.", "; if nothing was submitted and vector printing is unavailable, use 600 DPI raster."),
        ("-документ передаётся в ", "-документ передається до ", " document is sent to "),
        (". Драйвер может преобразовать его в растр.", ". Драйвер може перетворити його на растр.", ". The driver may rasterize it."),
        ("Не удалось открыть свойства драйвера ", "Не вдалося відкрити властивості драйвера ", "Could not open driver properties for "),
        ("Отправка страниц в ", "Надсилання сторінок до ", "Submitting pages to "),
        ("Лист ", "Аркуш ", "Sheet "),
        (" из ", " із ", " of "),
        (": печать лицевой стороны…", ": друк лицьового боку…", ": printing front…"),
        (": печать оборота…", ": друк звороту…", ": printing reverse…"),
        (" — переворот", " — перевертання", " — flip"),
        ("Ошибка печати: ", "Помилка друку: ", "Print error: "),
        (" Статусы карточек не изменены. Если задание могло уйти в ", " Стани карток не змінено. Якщо завдання могло потрапити до ", " Card statuses unchanged. If the job might have been sent to "),
        (", не повторяйте печать до проверки листов.", ", не повторюйте друк до перевірки аркушів.", ", do not retry until you check the sheets."),
        ("Брак: ", "Брак: ", "Failed: "),
        ("Позиция ", "Позиція ", "Position "),
        (" карточек · ", " карток · ", " cards · "),
        (" листов · ", " аркушів · ", " sheets · "),
        (" карточек", " карток", " cards"),
        (" листов", " аркушів", " sheets"),
        (" · Записей: ", " · Записів: ", " · Entries: "),
        (" · Лист · лицо", " · Аркуш · лицьовий бік", " · Sheet · front"),
        (" · Лист · оборот", " · Аркуш · зворот", " · Sheet · reverse"),
        ("Лист · лицо", "Аркуш · лицьовий бік", "Sheet · front"),
        ("Лист · оборот", "Аркуш · зворот", "Sheet · reverse"),
        ("Оборотная сторона", "Зворотний бік", "Reverse side"),
        ("Лицевая сторона", "Лицьовий бік", "Front side"),
        (" мс", " мс", " ms"),
        ("Ошибка сохранения списка поддоменов (", "Помилка збереження списку піддоменів (", "Could not save subdomain list ("),
    ];

    private static readonly (string Ru, (string Uk, string En) Options)[] Fragments =
        FragmentSource.OrderByDescending(x => x.Ru.Length)
            .Select(x => (x.Ru, (x.Uk, x.En))).ToArray();

    // Dialogs contain only explicitly created UI strings; their controls may be
    // updated later during printing. Re-apply after layout without resetting
    // bindings in the main window's compiled templates.
    public static void Enable(Window window, bool translateText)
    {
        window.Opened += (_, _) => Apply(window, translateText);
        // The main window can rebuild large print previews many times per second.
        // Its bound strings update via property notifications, so traversing its
        // entire visual tree on each layout pass would be wasteful.
        if (translateText)
            window.LayoutUpdated += (_, _) =>
            {
                if (window.IsVisible) Apply(window, translateText);
            };
    }

    // Flyouts are hosted in separate popup visual roots, so a MainWindow
    // traversal cannot see their tooltip controls. This helper never changes
    // Text or Content and therefore cannot translate user-defined captions.
    public static void TranslateTooltips(Control root, string language)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>().Prepend(root))
        {
            if (ToolTip.GetTip(control) is not string tooltip) continue;
            var localized = Tr(tooltip, language);
            if (localized != tooltip) ToolTip.SetTip(control, localized);
        }
    }

    public static void Apply(Window window, bool translateText)
    {
        TranslateWindowTitle(window);
        foreach (var control in window.GetVisualDescendants().OfType<Control>())
        {
            var tip = ToolTip.GetTip(control);
            if (tip is string tooltip)
            {
                string localized = Tr(tooltip);
                if (localized != tooltip) ToolTip.SetTip(control, localized);
            }
            if (control is MenuItem menu && menu.Header is string header)
            {
                string localized = Tr(header);
                if (localized != header) menu.Header = localized;
            }
            if (!translateText) continue;
            if (control is TextBlock text && text.Text is string current)
            {
                string localized = Tr(current);
                if (localized != current) text.Text = localized;
            }
            if (control is CheckBox check && check.Content is string caption)
            {
                string localized = Tr(caption);
                if (localized != caption) check.Content = localized;
            }
            if (control is TextBox input && input.PlaceholderText is string placeholder)
            {
                string localized = Tr(placeholder);
                if (localized != placeholder) input.PlaceholderText = localized;
            }
            if (control is Button button && button.Content is string buttonLabel)
            {
                string localized = Tr(buttonLabel);
                if (localized != buttonLabel) button.Content = localized;
            }
        }
    }

    private static void TranslateWindowTitle(Window window)
    {
        var current = window.Title ?? "";
        var localized = Tr(current);
        if (current != localized) window.Title = localized;
    }
}
