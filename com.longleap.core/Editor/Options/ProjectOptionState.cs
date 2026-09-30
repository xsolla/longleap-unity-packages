using System.Collections.Generic;
using UnityEditor;

namespace LongLeap.Core.Editor
{
    // Снимок проекта для чтения настроек. Смысл именно в снимке: набор
    // дефайнов читается ОДИН раз, SerializedObject кэшируется по одному на
    // ассет, поэтому опросить все настройки разом стоит столько же, сколько
    // одну. Без этого GamePixAsmdefConstraints читал бы два файла с диска на
    // каждую перерисовку окна.
    public interface IProjectReadContext
    {
        ISet<string> Defines { get; }

        // null, если ассета нет. Вызывающий обязан это проверить: ассет могли
        // не создать, переименовать или удалить.
        SerializedObject GetSerializedObject(string assetPath);
    }

    // Представление состояния, против которого считается доступность.
    //
    // Реализаций две, и это главный приём всей конструкции: меню передаёт
    // сохранённое состояние и ведёт себя точно как раньше, а окно — отложенное
    // (сохранённое плюс незаписанные правки), и строка гаснет сразу, как
    // только сняли галочку выше, ещё до Apply. Один интерфейс, два
    // представления — и меню с окном не могут разойтись в понимании того, что
    // такое «доступна».
    public interface IProjectOptionState
    {
        bool IsDefineOn(string symbol);

        // null, если настройка не зарегистрирована.
        string GetValue(string optionId);

        bool TryGetOption(string optionId, out ProjectOption option);
    }

    // Отложенное состояние, в которое можно писать, не трогая проект. Нужно
    // пресетам: они раскладывают значения по настройкам, а записью занимается
    // потом Apply.
    public interface IStagedOptionState : IProjectOptionState
    {
        void Set(string optionId, string value);

        // Вернуть одну настройку к сохранённому значению. Именно это, а не
        // «выключить», делает согласование пресета: недоступную настройку
        // надо оставить как была, иначе строка покажется изменённой, а Apply
        // полез бы её писать.
        void Clear(string optionId);
    }

    // Приёмник изменений. Ничего не пишет сам: НАКАПЛИВАЕТ и отдаёт всё одним
    // вызовом в Flush. Ради этого он и существует — пять галочек за один Apply
    // должны стоить один SetScriptingDefineSymbols и одну перезагрузку домена,
    // а не пять.
    public interface IProjectWriteContext
    {
        void SetDefine(string symbol, bool on);

        // null, если ассета нет. Возвращённый объект будет применён и сохранён
        // в Flush — руками ApplyModifiedProperties вызывать не нужно.
        SerializedObject GetSerializedObject(string assetPath);

        // Попросить AssetDatabase.Refresh в конце. Нужно тем, кто пишет файлы
        // в обход AssetDatabase — так делает ремонт asmdef-ов.
        void RequestAssetRefresh();

        void Log(string message);

        void LogError(string message);
    }
}
