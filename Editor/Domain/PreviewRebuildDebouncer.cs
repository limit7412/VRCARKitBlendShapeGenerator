using System;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// 連続して届く再生成要求を1回にまとめるための時刻計算。
    ///
    /// スライダーをドラッグしている間は要求が毎フレーム届く。そのたびに再生成すると
    /// 大きいアバターでは操作が重くなるため、要求が止むまで実行を先送りする。
    /// ただし先送りだけだと長いドラッグの間プレビューが固まって見えるので、
    /// 最初の要求からの上限で打ち切り、ドラッグ中も一定間隔では追従させる。
    ///
    /// 時刻を引数で受け取るのでUnityのAPIに依存しない（実際の駆動はInfra層が行う）。
    /// </summary>
    internal sealed class PreviewRebuildDebouncer
    {
        /// <summary>最後の要求からこれだけ変化が無ければ実行する</summary>
        public const double IdleDelaySeconds = 0.1;

        /// <summary>要求が続いていても、最初の要求からこれ以上は先送りしない</summary>
        public const double MaxDeferSeconds = 0.4;

        private readonly double _idleDelaySeconds;
        private readonly double _maxDeferSeconds;

        private bool _isPending;
        private double _firstRequestTime;
        private double _dueTime;

        public PreviewRebuildDebouncer()
            : this(IdleDelaySeconds, MaxDeferSeconds)
        {
        }

        public PreviewRebuildDebouncer(double idleDelaySeconds, double maxDeferSeconds)
        {
            _idleDelaySeconds = idleDelaySeconds;
            _maxDeferSeconds = maxDeferSeconds;
        }

        /// <summary>先送り中の要求があるか</summary>
        public bool IsPending => _isPending;

        /// <summary>先送り中の要求を実行する時刻（IsPendingがfalseのときは意味を持たない）</summary>
        public double DueTime => _dueTime;

        /// <summary>再生成を要求する。先送り中であれば実行時刻を延ばす</summary>
        public void Request(double now)
        {
            if (!_isPending)
            {
                _isPending = true;
                _firstRequestTime = now;
            }

            _dueTime = Math.Min(now + _idleDelaySeconds, _firstRequestTime + _maxDeferSeconds);
        }

        /// <summary>
        /// 実行時刻を過ぎていれば要求を取り下げてtrueを返す。
        /// 呼び出し側はtrueのときだけ再生成を走らせる
        /// </summary>
        public bool TryConsume(double now)
        {
            if (!_isPending || now < _dueTime)
            {
                return false;
            }

            _isPending = false;
            return true;
        }
    }
}
