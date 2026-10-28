using System.Collections.Generic;

namespace LongLeap.Customization
{
    /// <summary>
    /// Тот, кому панель кастомизации надевает скины — на деле спайновый персонаж.
    ///
    /// Панель знает про него ровно одно: «надень вот этот набор частей». Что такое
    /// скин, как смешать части в одну и когда перерисовать скелет — забота реализации,
    /// поэтому <see cref="CustomizationPanel"/> и вся сборка LongLeap.Customization.UI собираются
    /// без спайнового рантайма и не ломаются, пока его в проекте нет.
    ///
    /// Реализация висит на главном объекте спайна. Когда рантайм появится, она целиком
    /// выглядит так (отдельный файл в своей сборке, ссылающейся на spine-unity
    /// и на LongLeap.Customization.UI):
    /// <code>
    /// public sealed class SpineSkinTarget : MonoBehaviour, ISkinTarget
    /// {
    ///     [SerializeField] private SkeletonGraphic _spine;
    ///
    ///     public void SetSkins(IReadOnlyList&lt;string&gt; skins)
    ///     {
    ///         var skeleton = _spine.Skeleton;
    ///         var combined = new Skin("custom");
    ///
    ///         foreach (var name in skins)
    ///         {
    ///             var skin = skeleton.Data.FindSkin(name);
    ///             if (skin != null) combined.AddSkin(skin);
    ///         }
    ///
    ///         skeleton.SetSkin(combined);
    ///         skeleton.SetSlotsToSetupPose();      // новые аттачменты — в позу настройки
    ///         _spine.AnimationState.Apply(skeleton); // перерисовать текущий кадр, не ожидая следующего
    ///     }
    /// }
    /// </code>
    ///
    /// Если части лежат на разных страницах атласа, SkeletonGraphic нарисует их
    /// несколькими материалами — тогда собранный скин прогоняют через
    /// <c>GetRepackedSkin</c> один раз на выбор, а не каждый кадр.
    /// </summary>
    public interface ISkinTarget
    {
        /// <summary>
        /// Надеть перечисленные скины разом — по одному на категорию
        /// (доска, голова, торс, ноги), в порядке категорий панели.
        ///
        /// Именно «весь набор», а не «замени одну часть»: смешанный скин всё равно
        /// собирается заново, а панель всегда знает полный выбор игрока.
        ///
        /// Список живёт до конца вызова и переиспользуется дальше — если нужен
        /// потом, копируй.
        /// </summary>
        void SetSkins(IReadOnlyList<string> skins);
    }
}
