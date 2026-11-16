using System;
using ProtoHarness.ChainRush.Bots;

namespace ProtoHarness.ChainRush.Flow.Lobby
{
    // The three bots share one difficulty, selected before the speed class.
    public sealed class DifficultyPage : LobbyPage
    {
        private static readonly string[] Labels = { "쉬움", "보통", "어려움" };
        private static readonly string[] Descriptions =
        {
            "기본 주행 봇과 순위를 겨룹니다.\n봇전에 익숙해지기 좋은 난이도입니다.",
            "보통 난이도의 봇 3명과 순위를 겨룹니다.\n코스의 기술을 활용하는 주행을 준비합니다.",
            "어려움 난이도의 봇 3명과 순위를 겨룹니다.\n더 빠른 주행에 도전하는 난이도입니다.",
        };

        public DifficultyPage(LobbyContext context) : base(context, LobbyMenu.Page.Difficulty, "DIFFICULTY") { }
        public override int Count => Labels.Length;
        public override string Label(int row) => Labels[row];
        public override string DescriptionTag => "BOT DIFFICULTY";
        public override string Description(int row) => Descriptions[row];

        public override Step Confirm(int row)
        {
            if (row < 0 || row >= Count) throw new ArgumentOutOfRangeException(nameof(row));
            Context.Difficulty = (BotDifficulty)row;
            return Step.Open(LobbyMenu.Page.Speed, Context.Speed);
        }

        public override Step Back() => Step.Open(LobbyMenu.Page.Mode, SingleModePage.BotsRow);
    }
}
