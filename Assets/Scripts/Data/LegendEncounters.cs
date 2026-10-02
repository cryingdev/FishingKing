using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingKing
{
    /// <summary>
    /// The legends' encounter rows (Docs/lures_legend_spec.md 2.1, Docs/legends_rollout.md 3): tuned feel values tied to
    /// EncounterView's choreography branches, so each legend keeps a factory here. A species file names its factory with
    /// "encounter" (Resources/Data/Fish/&lt;id&gt;.json, Docs/data_reference.md 2.5); the loader (SpeciesData) makes a fresh
    /// row and fills its key lures from the species' baits, in their order: a legend's baits are its key lures, so the
    /// factories leave keyLures empty (the validator checks it).
    /// </summary>
    public static class LegendEncounters
    {
        static readonly Dictionary<string, Func<EncounterDef>> factories = new Dictionary<string, Func<EncounterDef>>
        {
            { "golden_carp", GoldenCarp },
            { "arapaima", Arapaima },
            { "sturgeon", Sturgeon },
            { "blue_marlin", BlueMarlin },
            { "great_white", GreatWhite },
            { "coelacanth", Coelacanth },
        };

        /// <summary>The factory keys (the species ids of the legends).</summary>
        public static IReadOnlyCollection<string> Keys => factories.Keys;

        /// <summary>A new row for this legend (a fresh instance on every call), or null for an unknown key.</summary>
        public static EncounterDef Make(string key) => key != null && factories.TryGetValue(key, out var f) ? f() : null;

        /// <summary>The values every rollout row shares (Docs/legends_rollout.md 2).</summary>
        static EncounterDef Row(string id, string backdrop, string eyeCore, string eyeGlow, string frameLine)
        {
            return new EncounterDef
            {
                model = "legend_" + id, backdrop = backdrop, eyeCore = eyeCore, eyeGlow = eyeGlow, frameLine = frameLine,
                gaugeStart = 20, pityStep = 10, pityMax = 30, decay = 3f, timeoutStrike = 60,
                appear = new Vector2(20f, 40f), relocate = new Vector2(90f, 150f), coolFail = 60f, coolFight = 180f,
                tell = 0.35f, perfectT = 0.25f, buffer = 0.15f, bottomBand = 99f, moodHold = 2.5f,
            };
        }

        static MoodDef HoldMood(string prompt, string credit, float grace, float gain) => new MoodDef
        {
            name = "흥분", prompt = prompt, verb = Verb.Hold, lo = grace, gain = gain, flickLoss = 20f, circleGrace = 0.25f,
            circleLoss = 15f, creditText = credit,
        };

        /// <summary>황금잉어: the lake's bait-eater; a bait lying still on the bottom by the weed edge (rollout 3.1).</summary>
        static EncounterDef GoldenCarp()
        {
            var e = Row("golden_carp", "lake", "#fff6d0", "#ffc830", "#a8e878");
            e.nameSub = "백 년을 산 호수의 주인";
            e.tipWrongLure = "바닥에 가만히 놓인 달콤한 먹이를 좋아하는 것 같다…";
            e.meterQ = MeterQ.Still;
            e.keyRules["bait_softworm"] = new KeyRule { meterQ = MeterQ.Lure };
            e.depthMin = 4.0f;
            e.bottomBand = 0.8f;
            e.lurkZMin = 14f;
            e.lurkZMax = 30f;
            e.nearLurk = 7f;
            e.lurkWeedEdge = true;   // (on the generated bed: the drop-off beside the weed, rollout 3.1)
            e.minQ = 0.6f;
            e.minSoak = 10f;
            e.fillTime = 18f;
            e.chance = 0.6f;
            e.minLine = 11f;
            e.teaseLimit = 30f;
            e.noseIn = new Vector2(1.0f, 1.6f);
            e.fakeOuts = 1;
            e.fakeOutText = "…맛을 본다";
            e.hookWindow = 0.7f;
            e.lightGlow = e.lightPlain = 3.6f;
            e.orbitFar = 2.2f;
            e.orbitNear = 1.1f;
            e.noseDist = 0.3f;
            // the tasting up close: the camera moves in until the carp fills about half the window
            e.noseFill = new Vector2(0.52f, 0.78f);
            e.noseFrame = new Vector2(0.5f, 0.55f);
            e.noseCamTease = 0.35f;
            e.moods = new[]
            {
                new MoodDef { name = "경계", prompt = "살살… 아주 천천히 끌어요", verb = Verb.Wind, lo = 0.1f, hi = 0.4f, gain = 8f,
                    tooFast = 0.8f, fastLoss = 12f, flickLoss = 15f, creditText = "좋아요" },
                new MoodDef { name = "호기심", prompt = "살짝 끌고… 멈춰요", verb = Verb.RunPause, lo = 0.1f, hi = 0.5f, gain = 15f,
                    runMin = 0.3f, runMax = 1.2f, pauseMin = 1.0f, pauseMax = 2.5f, tooFast = 0.8f, fastLoss = 10f,
                    overRun = 2.0f, overRunLoss = 6f, overRunText = "멈춰요, 살짝만!", boredAfter = 4f, boredLoss = 4f, flickLoss = 12f,
                    creditText = "따라와요!" },
                HoldMood("멈춰요! 빨아들이려 해요", "입술을 내민다…!", 0.5f, 11f),
            };
            e.tellText = "…입술이 움직인다";
            e.lungeT = 0.45f;
            e.hitStop = 0.10f;
            e.biteText = "쪼옥!";
            e.lurkText = "물속 깊은 곳에서 묵직한 그림자가 스르륵 지나갔다…";
            e.eyesText = "물풀 너머에서 무언가 다가온다…";
            e.lostText = "황금잉어가 물풀 속으로 사라졌다…";
            e.failTips = new[] { "다음엔: 미끼는 아주 살짝만 끌기!", "다음엔: 톡 당기지 말고 끌기", "다음엔: 흥분하면 멈추기!", "다음엔: 맛볼 때는 참았다가 챔질" };
            e.catchLine = "비늘 하나하나가 금화처럼 빛난다.";
            e.choreo = new Choreo
            {
                style = ChoreoStyle.Carp, swim = new[] { 2f, 5f, 8f, 11f, 16f }, tailHz = new Vector4(0.6f, 0.8f, 0.5f, 1.8f), cruise = 0.9f,
                eyes0 = new Vector3(4.6f, 0.3f, 7.6f), eyes1 = new Vector3(3.4f, 0.25f, 5.6f), deepDir = new Vector3(0.55f, 0f, 0.83f),
                orbitSpeed = new Vector2(0.30f, 0.45f), orbitDepth = 0.35f, hover = new Vector2(0.12f, 30f),
                finAmp = 0.8f, flare = 25f, scull = 12f, barbelSign = -1f,
            };
            return e;
        }

        /// <summary>피라루쿠: the swamp's surface-lure legend, striking from below (rollout 3.2).</summary>
        static EncounterDef Arapaima()
        {
            var e = Row("arapaima", "swamp", "#ffe6c8", "#ff7a3a", "#d8e070");
            e.nameSub = "숨 쉬러 떠오르는 늪의 거인";
            e.tipWrongLure = "수면에서 퍼덕이는 먹이를 노리는 것 같다…";
            e.depthMin = 0f;
            e.depthMax = 0.4f;
            e.lurkZMin = 10f;
            e.lurkZMax = 30f;
            e.nearLurk = 7f;
            e.lurkDepth = 1.2f;
            e.minQ = 0.55f;
            e.minSoak = 6f;
            e.fillTime = 16f;
            e.chance = 0.6f;
            e.minLine = 22f;
            e.teaseLimit = 28f;
            e.noseIn = new Vector2(0.8f, 1.4f);
            e.hookWindow = 0.5f;
            e.lightGlow = e.lightPlain = 2.2f;
            e.orbitFar = 2.0f;
            e.orbitNear = 1.2f;
            e.noseDist = 0.8f;
            e.camScale = 1.15f;
            e.moods = new[]
            {
                new MoodDef { name = "경계", prompt = "개구리처럼… 살살 감다 멈춰요", verb = Verb.RunPause, lo = 0.3f, hi = 0.8f, gain = 12f,
                    runMin = 0.5f, runMax = 2.0f, pauseMin = 0.5f, pauseMax = 2.0f, tooFast = 1.2f, fastLoss = 12f,
                    overRun = 2.5f, overRunLoss = 6f, boredAfter = 3.5f, boredLoss = 4f, flickLoss = 8f, creditText = "좋아요" },
                new MoodDef { name = "호기심", prompt = "톡! 퐁— 물결이 잦아들 때까지", verb = Verb.FlickPause, lo = 1.0f, hi = 2.5f, gain = 14f,
                    maxStrength = 1f, strongAt = 0.95f, strongLoss = 8f, hurryLoss = 6f, circleAfter = 1.5f, circleLoss = 6f,
                    boredAfter = 3.5f, boredLoss = 5f, creditText = "올려다봐요!" },
                HoldMood("멈춰요! 바로 밑에 있어요", "떠오른다…!", 0.6f, 12f),
            };
            e.tellText = "…아래에서 노려본다";
            e.lungeT = 0.20f;
            e.hitStop = 0.14f;
            e.shake = new Vector2(0.25f, 0.25f);
            e.biteText = "펑!";
            e.lurkText = "탁한 물속에서 무언가 숨을 쉬러 떠올랐다…";
            e.eyesText = "탁한 물 아래에서 붉은 눈이 떠오른다…";
            e.lostText = "피라루쿠가 탁한 물속으로 가라앉았다…";
            e.failTips = new[] { "다음엔: 감다가 꼭 멈추기", "다음엔: 톡 당긴 뒤 물결이 잦아들 때까지", "다음엔: 흥분하면 멈추기!", "다음엔: 솟구치기 전엔 챔질 참기" };
            e.catchLine = "붉은 비늘이 달아오른 숯불처럼 번진다.";
            e.choreo = new Choreo
            {
                style = ChoreoStyle.Arapaima, swim = new[] { 2f, 4f, 7f, 10f, 14f }, tailHz = new Vector4(0.5f, 0.7f, 0.9f, 3.0f), cruise = 0.8f,
                eyes0 = new Vector3(2.4f, -2.4f, 5.2f), eyes1 = new Vector3(1.8f, -1.8f, 4.0f), deepDir = new Vector3(0.55f, -0.45f, 0.7f),
                orbitSpeed = new Vector2(0.28f, 0.5f), orbitDepth = -0.8f, hover = new Vector2(-0.7f, -50f),
                finAmp = 0.6f, flare = 25f, scull = 10f,
            };
            // the frog and the popper are topwater lures: the tease is watched from above, the legend a shadow under the
            // murk (each has its top frames, lure_frog_top_* / lure_popper_top_*; a key without them keeps the side view)
            e.teaseView = TeaseView.Top;
            e.top = new TopViewDef { set = "swamp_top", noseInText = "떠오른다…!", holdCredit = "…아래에서 노려본다" };
            return e;
        }

        /// <summary>철갑상어: under the ice, a worm dragged and hopped along the bottom by the hole (rollout 3.3).</summary>
        static EncounterDef Sturgeon()
        {
            var e = Row("sturgeon", "ice", "#f0fbff", "#9ad8ff", "#bfefff");
            e.nameSub = "공룡과 함께 살았던 갑옷 물고기";
            e.tipWrongLure = "바닥을 천천히 기어가는 먹이를 찾는 것 같다…";
            e.depthMin = 3.5f;
            e.bottomBand = 0.8f;
            e.lurkZMin = 6f;
            e.lurkZMax = 14f;
            e.nearLurk = 12f;
            e.minQ = 0.5f;
            e.minSoak = 8f;
            e.fillTime = 18f;
            e.chance = 0.6f;
            e.minLine = 22f;
            e.relocate = new Vector2(120f, 180f);
            e.teaseLimit = 30f;
            e.noseIn = new Vector2(1.2f, 1.8f);
            e.fakeOuts = 1;
            e.fakeOutText = "…수염으로 더듬는다";
            e.hookWindow = 0.7f;
            e.lightGlow = e.lightPlain = 3.0f;
            e.orbitFar = 2.6f;
            e.orbitNear = 1.4f;
            e.noseDist = 0.35f;
            e.camScale = 1.1f;
            e.moods = new[]
            {
                new MoodDef { name = "경계", prompt = "살살… 바닥을 천천히 끌어요", verb = Verb.Wind, lo = 0.1f, hi = 0.35f, gain = 8f,
                    tooFast = 0.7f, fastLoss = 12f, flickLoss = 15f, creditText = "좋아요" },
                new MoodDef { name = "호기심", prompt = "바닥에서 살짝 톡… 기다려요", verb = Verb.FlickPause, lo = 1.2f, hi = 3.0f, gain = 14f,
                    maxStrength = 0.5f, strongAt = 0.7f, strongLoss = 10f, hurryLoss = 6f, circleAfter = 1.5f, circleLoss = 6f,
                    boredAfter = 4f, boredLoss = 4f, creditText = "더듬어 봐요!" },
                HoldMood("멈춰요! 수염이 닿았어요", "입이 내려온다…!", 0.6f, 10f),
            };
            e.tellText = "…입이 내려온다";
            e.lungeT = 0.30f;
            e.biteText = "후읍!";
            e.lurkText = "얼음 아래에서 무언가 바닥을 훑고 지나갔다…";
            e.eyesText = "얼음 밑 어둠 속에서 무언가 다가온다…";
            e.lostText = "철갑상어가 얼음 밑 어둠으로 사라졌다…";
            e.failTips = new[] { "다음엔: 바닥은 아주 천천히 끌기!", "다음엔: 톡은 아주 살짝", "다음엔: 흥분하면 멈추기!", "다음엔: 수염이 더듬을 땐 참기" };
            e.catchLine = "등의 뼈 비늘이 갑옷처럼 단단하다.";
            e.choreo = new Choreo
            {
                style = ChoreoStyle.Sturgeon, swim = new[] { 1f, 3f, 6f, 9f, 14f }, tailHz = new Vector4(0.45f, 0.6f, 0.5f, 1.5f), cruise = 0.6f,
                eyes0 = new Vector3(5.0f, 0.2f, 7.0f), eyes1 = new Vector3(3.8f, 0.2f, 5.0f), deepDir = new Vector3(0.7f, 0f, 0.7f),
                orbitSpeed = new Vector2(0.25f, 0.35f), orbitDepth = 0.25f, hover = new Vector2(0.06f, 6f),
                finAmp = 0.5f, flare = 20f, scull = 10f, barbelSign = 1f, barbelRoll = true,
            };
            return e;
        }

        /// <summary>청새치: the ocean's speed legend: a kona run fast near the surface (rollout 3.4, 5).</summary>
        static EncounterDef BlueMarlin()
        {
            var e = Row("blue_marlin", "ocean", "#e8f8ff", "#4ab0ff", "#7fd4ff");
            e.nameSub = "대양을 가르는 푸른 창";
            e.tipWrongLure = "빠르게 달아나는 먹이를 쫓는 것 같다…";
            e.depthMax = 3.0f;
            e.lurkZMin = 20f;
            e.lurkZMax = 45f;
            e.nearLurk = 10f;
            // (long casts off the boat: a wider spot, a little longer to reel in and throw)
            e.spotRadius = 2.0f;
            e.spotWindow = 14f;
            e.lurkDepth = 6f;
            e.minQ = 0.6f;
            e.minSoak = 5f;
            e.fillTime = 14f;
            e.chance = 0.6f;
            e.minLine = 45f;
            e.teaseLimit = 26f;
            e.noseIn = new Vector2(0.6f, 1.0f);
            e.fakeOuts = 1;
            e.fakeOutText = "휙— 부리로 친다!";
            e.hookWindow = 0.5f;
            e.lightGlow = e.lightPlain = 6.0f;
            e.orbitFar = 3.0f;
            e.orbitNear = 1.5f;
            e.noseDist = 0.9f;
            e.camScale = 1.2f;
            e.moods = new[]
            {
                new MoodDef { name = "경계", prompt = "빠르게! 쉬지 말고 감아요", verb = Verb.Wind, lo = 1.6f, hi = 9f, gain = 9f,
                    tooSlow = 1.2f, slowAfter = 0.5f, slowLoss = 8f, slowText = "느려요! 계속 달려요", flickLoss = 10f, creditText = "좋아요" },
                new MoodDef { name = "호기심", prompt = "휙 감다가 툭! 멈췄다 다시", verb = Verb.RunPause, lo = 1.8f, hi = 9f, gain = 13f,
                    runMin = 0.5f, runMax = 1.4f, pauseMin = 0.3f, pauseMax = 1.0f, overRun = 2.2f, overRunLoss = 4f,
                    overRunText = "툭 멈췄다 다시!", boredAfter = 1.6f, boredLoss = 8f, boredText = "너무 오래 멈췄어요", flickLoss = 10f,
                    creditText = "몸이 빛나요!" },
                HoldMood("멈춰요! 먹이를 놓아줘요", "푸른 줄무늬가 타오른다…!", 0.3f, 14f),
            };
            e.tellText = "…돛을 세운다";
            e.lungeT = 0.25f;
            e.hitStop = 0.10f;
            e.biteText = "콱!";
            e.lurkText = "수평선 아래로 기다란 그림자가 빠르게 스쳐 갔다…";
            e.eyesText = "깊고 푸른 곳에서 무언가 치솟는다…";
            e.lostText = "청새치가 푸른 바다 너머로 사라졌다…";
            e.failTips = new[] { "다음엔: 경계할 땐 쉬지 말고 빠르게!", "다음엔: 감다가 짧게만 멈추기", "다음엔: 흥분하면 멈추기!", "다음엔: 부리로 칠 땐 참았다가 챔질" };
            e.catchLine = "푸른 줄무늬가 아직도 희미하게 빛난다.";
            e.choreo = new Choreo
            {
                style = ChoreoStyle.Marlin, swim = new[] { 0.5f, 1f, 2f, 5f, 18f }, tailHz = new Vector4(1.2f, 1.6f, 1.4f, 3.5f), cruise = 3.0f,
                eyes0 = new Vector3(2.5f, -3.6f, 9.0f), eyes1 = new Vector3(1.5f, -2.0f, 6.0f), deepDir = new Vector3(0.5f, -0.5f, 0.7f),
                orbitSpeed = new Vector2(0.5f, 0.9f), orbitDepth = -0.2f, hover = new Vector2(0f, 0f),
                finAmp = 0f, flare = 35f, scull = 5f,
            };
            return e;
        }

        /// <summary>백상아리: the ocean's stalker: a kona crawled like a crippled fish, or a jig worked deep (rollout 3.5, 5).</summary>
        static EncounterDef GreatWhite()
        {
            var e = Row("great_white", "ocean", "#f4f8fa", "#9aa8b4", "#7fd4ff");
            e.nameSub = "바다의 절대 포식자";
            e.tipWrongLure = "천천히 비틀거리는 먹이를 노리는 것 같다…";
            e.meterQ = MeterQ.Crawl;
            e.keyRules["bait_jig"] = new KeyRule { depthMin = 6f, meterQ = MeterQ.Lure };
            e.lurkZMin = 20f;
            e.lurkZMax = 45f;
            e.nearLurk = 10f;
            e.spotRadius = 2.0f;
            e.spotWindow = 14f;
            e.lurkDepth = 6f;
            e.minQ = 0.55f;
            e.minSoak = 6f;
            e.fillTime = 20f;
            e.chance = 0.55f;
            e.minLine = 100f;
            e.gearLine = "금속 줄";
            e.coolFail = 90f;
            e.coolFight = 240f;
            e.teaseLimit = 30f;
            e.noseIn = new Vector2(1.0f, 1.6f);
            e.fakeOuts = 1;
            e.fakeOutText = "쿵… 코로 들이받는다";
            e.hookWindow = 0.6f;
            e.lightGlow = e.lightPlain = 6.0f;
            e.orbitFar = 3.4f;
            e.orbitNear = 2.0f;
            e.noseDist = 1.5f;
            e.camScale = 1.4f;
            e.moods = new[]
            {
                new MoodDef { name = "경계", prompt = "비틀비틀… 천천히 감아요", verb = Verb.Wind, lo = 0.4f, hi = 1.2f, gain = 8f,
                    tooFast = 1.8f, fastLoss = 10f, tooSlow = 0.25f, slowAfter = 1.0f, slowLoss = 4f, slowText = "계속 비틀비틀 감아요",
                    flickLoss = 12f, creditText = "좋아요" },
                new MoodDef { name = "호기심", prompt = "톡! 다친 물고기처럼… 멈춰요", verb = Verb.FlickPause, lo = 0.8f, hi = 2.0f, gain = 13f,
                    maxStrength = 0.7f, strongAt = 0.85f, strongLoss = 10f, hurryLoss = 6f, circleAfter = 1.5f, circleLoss = 6f,
                    boredAfter = 3f, boredLoss = 5f, creditText = "다가와요…" },
                HoldMood("멈춰요! 움직이면 끝이에요", "입을 벌린다…!", 0.5f, 11f),
            };
            e.tellText = "…눈이 뒤집힌다";
            e.lungeT = 0.30f;
            e.hitStop = 0.15f;
            e.shake = new Vector2(0.3f, 0.3f);
            e.biteText = "콰직!";
            e.lurkText = "배 밑으로 거대한 그림자가 지나갔다…";
            e.eyesText = "푸른 어둠 아래에서 거대한 그림자가 떠오른다…";
            e.lostText = "백상아리가 푸른 어둠 속으로 가라앉았다…";
            e.failTips = new[] { "다음엔: 경계할 땐 천천히 비틀비틀", "다음엔: 톡은 짧게, 너무 세지 않게", "다음엔: 흥분하면 멈추기!", "다음엔: 들이받을 땐 참았다가 챔질" };
            e.catchLine = "이빨 한 줄 한 줄이 칼날처럼 늘어서 있다.";
            e.choreo = new Choreo
            {
                style = ChoreoStyle.GreatWhite, swim = new[] { 1f, 2f, 4f, 7f, 14f }, tailHz = new Vector4(0.35f, 0.45f, 0.55f, 2.2f), cruise = 1.0f,
                eyes0 = new Vector3(1.6f, -2.9f, 9.5f), eyes1 = new Vector3(1.4f, -2.1f, 5.6f), deepDir = new Vector3(0.3f, -0.8f, 0.5f),
                orbitSpeed = new Vector2(0.22f, 0.30f), orbitDepth = -0.6f, hover = new Vector2(0f, 0f),
                finAmp = 0.2f, flare = 12f, scull = 0f, cruiseJaw = 5f,
            };
            return e;
        }

        /// <summary>The cave coelacanth's encounter row (spec 2.1 / 2.8 / 2.9).</summary>
        static EncounterDef Coelacanth()
        {
            var e = new EncounterDef
            {
                model = "legend_coelacanth", backdrop = "cave", nameSub = "4억 년을 살아남은 고대어",
                tipWrongLure = "어둠 속에서 빛나며 가라앉는 먹이에 끌리는 것 같다…",
                depthMin = 5.0f, bottomBand = 1.5f, lurkZMin = 14f, lurkZMax = 30f, nearLurk = 8f,
                minQ = 0.55f, minSoak = 8f, fillTime = 16f, chance = 0.6f, minLine = 20f,
                coolFail = 60f, coolFight = 180f, appear = new Vector2(20f, 40f), relocate = new Vector2(90f, 150f),
                gaugeStart = 20, pityStep = 10, pityMax = 30, decay = 3f, teaseLimit = 28f, timeoutStrike = 60,
                noseIn = new Vector2(0.6f, 1.4f), tell = 0.35f, hookWindow = 0.6f, perfectT = 0.25f, buffer = 0.15f,
                // lure-light radius: the spec's 1.6 / 1.0 m leave the 1.3 m tease orbit almost black with the
                // smoothstep(0.35 R, R) falloff; 3.0 m lights it (0.95 at 1.3 m, the spots glint first at 2.4 m, none at 3 m)
                lightGlow = 3.0f, lightPlain = 2.0f,
                orbitFar = 2.4f, orbitNear = 1.3f, noseDist = 0.6f,
                eyeCore = "#f0ffd8", eyeGlow = "#b8ff8a", frameLine = "#6affea",
                moods = new[]
                {
                    new MoodDef { name = "경계", prompt = "살살… 천천히 감아요", verb = Verb.Wind, lo = 0.15f, hi = 0.7f, gain = 9f,
                        tooFast = 1.2f, fastLoss = 12f, flickLoss = 15f },
                    new MoodDef { name = "호기심", prompt = "톡! 당기고 기다려요", verb = Verb.FlickPause, lo = 0.6f, hi = 2.0f, gain = 14f,
                        maxStrength = 0.6f, strongAt = 0.8f, strongLoss = 10f, hurryLoss = 6f, circleAfter = 1.5f, circleLoss = 6f,
                        boredAfter = 3f, boredLoss = 5f },
                    new MoodDef { name = "흥분", prompt = "멈춰요! 움직이지 마요", verb = Verb.Hold, lo = 0.4f, gain = 12f,
                        flickLoss = 20f, circleGrace = 0.25f, circleLoss = 15f },
                },
            };
            return e;
        }
    }
}
