using System;
using System.Collections.Generic;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Инвентарь игрока: какие скины у него открыты и в какой категории лежит
    /// каждый.
    ///
    /// Скин — уникальная вещь: он либо есть, либо нет, количества у него не
    /// бывает. Поэтому здесь нет счётчиков — только <see cref="Has"/> и
    /// <see cref="Grant"/>.
    ///
    /// Отобрать скин нельзя намеренно: обратной операции в игре не существует
    /// (за скин заплатили или его выиграли), а единственный, кому нужно всё
    /// стереть, — это разработчик, и у него для этого чит в меню Tools, а не
    /// метод в игровом контракте. Понадобится отъём (временный скин, аренда) —
    /// добавится отдельным методом с внятным именем.
    ///
    /// Категории и состав приходят из каталога (<see cref="ISkinCatalog"/>) —
    /// это контент, одинаковый у всех. Инвентарь поверх него отвечает, что из
    /// этого открыто.
    /// </summary>
    public interface IInventoryService
    {
        /// <summary>Категории кастомизации в порядке каталога: доска, голова, торс, ноги.</summary>
        IReadOnlyList<SkinCategoryId> Categories { get; }

        /// <summary>
        /// Все скины категории, открытые и нет, — из каталога. По нему панель
        /// кастомизации рисует варианты, а закрытые показывает замком.
        /// </summary>
        IReadOnlyList<SkinId> GetSkins(SkinCategoryId category);

        /// <summary>
        /// Открытые скины категории, в порядке каталога. Список пересобирается
        /// при изменениях, поэтому держать его у себя между кадрами не стоит —
        /// спрашивай заново по <see cref="Changed"/>.
        /// </summary>
        IReadOnlyList<SkinId> GetOwned(SkinCategoryId category);

        /// <summary>
        /// Заголовок категории из каталога: HEAD, BODY, LEGS, BOARD. Им подписана
        /// строка категории в панели кастомизации — строки создаются по каталогу,
        /// поэтому подпись приходит оттуда же.
        /// </summary>
        string GetCategoryTitle(SkinCategoryId category);

        /// <summary>
        /// Подпись варианта из каталога — то, что видит игрок. Пусто в каталоге —
        /// вернётся имя скина.
        /// </summary>
        string GetTitle(SkinId skin);

        /// <summary>
        /// Дефолтный скин категории: её резерв, открытый у игрока всегда. На него
        /// откатывается надетое, когда надеть больше нечего.
        /// </summary>
        SkinId GetDefault(SkinCategoryId category);

        /// <summary>Стартовый набор категории — см. <see cref="ISkinCatalog.GetStartingOutfit"/>.</summary>
        IReadOnlyList<SkinId> GetStartingOutfit(SkinCategoryId category);

        /// <summary>Открыт ли скин у игрока.</summary>
        bool Has(SkinId skin);

        /// <summary>В какой категории лежит скин. Нет в каталоге — false.</summary>
        bool TryGetCategory(SkinId skin, out SkinCategoryId category);

        /// <summary>
        /// Зоны перекраса для группы скинов (Hair, Heads…) — из
        /// каталога. Панель кастомизации по ним решает, есть ли у группы кнопка
        /// «color» и что она красит. Пусто — кнопки не будет.
        /// </summary>
        IReadOnlyList<string> GetColorZones(string group);

        /// <summary>Можно ли снять последний вариант группы — см. <see cref="ISkinCatalog.CanGroupBeOff"/>.</summary>
        bool CanGroupBeOff(string group);

        /// <summary>Можно ли надеть несколько вариантов группы сразу — см. <see cref="ISkinCatalog.IsGroupMultiSelect"/>.</summary>
        bool IsGroupMultiSelect(string group);

        /// <summary>Цвет по умолчанию для скина — см. <see cref="ISkinCatalog.TryGetDefaultColor"/>.</summary>
        bool TryGetDefaultColor(SkinId skin, out Color color);

        /// <summary>Иконка скина в панели кастомизации — см. <see cref="ISkinCatalog.GetIcon"/>.</summary>
        Sprite GetIcon(SkinId skin);

        /// <summary>
        /// Выдать скин. Возвращает true, если его открыли именно сейчас, и
        /// false, если он уже был: по этому ответу показывают «новый скин!» и
        /// не показывают его второй раз.
        ///
        /// Скина нет в каталоге — исключение: это опечатка в имени, и молчаливая
        /// выдача несуществующего скина всплыла бы как «награда не пришла».
        /// Скин отключён (<see cref="SkinAvailability.Disabled"/>) — false с
        /// предупреждением: это штатное состояние контента, а не ошибка в коде.
        /// </summary>
        bool Grant(SkinId skin);

        /// <summary>
        /// Состав инвентаря изменился — перечитай <see cref="GetOwned"/>.
        /// Летит и на выдачу скина, и на чит-очистку.
        ///
        /// Без аргумента намеренно: тот, кто выдал скин, и так знает, какой
        /// именно, а экранам нужно одно — обновиться.
        /// </summary>
        event Action Changed;
    }
}
