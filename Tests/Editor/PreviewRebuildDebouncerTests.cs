using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// スライダー操作中の再生成をまとめる時刻計算の検証。
    ///
    /// 「要求が止むまで待つ」だけだと長いドラッグの間プレビューが固まって見えるため、
    /// 最初の要求からの上限で打ち切る。その2つの規則が両方効いていることを固定化する。
    /// </summary>
    public class PreviewRebuildDebouncerTests
    {
        private const double IdleDelay = 0.1;
        private const double MaxDefer = 0.4;

        private static PreviewRebuildDebouncer CreateDebouncer()
        {
            return new PreviewRebuildDebouncer(IdleDelay, MaxDefer);
        }

        [Test]
        public void TryConsume_ReturnsFalse_WhenNothingIsRequested()
        {
            var debouncer = CreateDebouncer();

            Assert.That(debouncer.IsPending, Is.False);
            Assert.That(debouncer.TryConsume(100.0), Is.False);
        }

        [Test]
        public void TryConsume_WaitsForTheIdleDelay_AfterASingleRequest()
        {
            var debouncer = CreateDebouncer();
            debouncer.Request(100.0);

            Assert.That(debouncer.TryConsume(100.0 + IdleDelay - 0.01), Is.False);
            Assert.That(debouncer.TryConsume(100.0 + IdleDelay), Is.True);
        }

        [Test]
        public void Request_PushesBackTheDueTime_WhileRequestsKeepArriving()
        {
            // ドラッグ中は要求が届き続ける。その間は実行しない
            var debouncer = CreateDebouncer();
            debouncer.Request(100.0);
            debouncer.Request(100.05);

            Assert.That(debouncer.TryConsume(100.1), Is.False);
            Assert.That(debouncer.TryConsume(100.15), Is.True);
        }

        [Test]
        public void Request_StopsPushingBack_AtTheMaximumDeferral()
        {
            // 要求が途切れなくても、最初の要求からMaxDeferで打ち切って追従させる
            var debouncer = CreateDebouncer();
            for (double now = 100.0; now <= 100.0 + MaxDefer; now += 0.02)
            {
                debouncer.Request(now);
            }

            Assert.That(debouncer.DueTime, Is.EqualTo(100.0 + MaxDefer).Within(0.0001));
            Assert.That(debouncer.TryConsume(100.0 + MaxDefer), Is.True);
        }

        [Test]
        public void TryConsume_ClearsTheRequest_SoItDoesNotFireTwice()
        {
            var debouncer = CreateDebouncer();
            debouncer.Request(100.0);

            Assert.That(debouncer.TryConsume(100.5), Is.True);
            Assert.That(debouncer.IsPending, Is.False);
            Assert.That(debouncer.TryConsume(100.6), Is.False);
        }

        [Test]
        public void Request_StartsANewDeferral_AfterThePreviousOneIsConsumed()
        {
            // 実行後の要求は、前回の上限を引きずらずに測り直す
            var debouncer = CreateDebouncer();
            debouncer.Request(100.0);
            debouncer.TryConsume(100.0 + IdleDelay);

            debouncer.Request(200.0);

            Assert.That(debouncer.TryConsume(200.0 + IdleDelay - 0.01), Is.False);
            Assert.That(debouncer.TryConsume(200.0 + IdleDelay), Is.True);
        }
    }
}
