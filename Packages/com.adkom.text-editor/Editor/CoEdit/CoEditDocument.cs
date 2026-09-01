// =============================================================================
//  ADKOM Text Editor — per-document co-editing state (contract v1.0 §5, §6.1).
//
//  The algorithm is the standard operational transform against a total order,
//  and it is exact rather than best-effort, which is why this class keeps a
//  SERVER-SPACE copy of the document alongside the editor's buffer:
//
//    serverText  — the document as of the last sequenced op we incorporated,
//                  with none of our un-acked edits applied.
//    pending     — our batches sent but not yet acked, in order.
//    the buffer  — serverText with pending applied; that is what the user sees.
//
//  A remote batch arrives stamped with the sequence it was given and the base
//  it was authored against. Everything sequenced between that base and now is
//  exactly what its author had not seen, so transforming past those ops lifts
//  it into our server space; transforming it past `pending` then lifts it into
//  the user's space. Our own ops enter server space when their ack arrives.
//  Two documents' worth of memory is the price, and documents are capped at
//  1 MB by the contract, so it is a cheap price for provable convergence.
// =============================================================================

using System.Collections.Generic;

namespace ADKOM.TextEditor.CoEdit
{
    internal sealed class CoEditDocument
    {
        /// <summary>How much sequenced history to retain for lifting late
        /// batches. A sender more than this many ops behind is impossible in
        /// practice: ALS acks every batch within a round trip.</summary>
        const int HistoryCap = 512;

        public readonly string Path;
        public bool ReadOnly;

        string _serverText = "";
        long _serverSeq = -1;

        readonly List<(long seq, EditBatch batch)> _history = new List<(long, EditBatch)>();
        readonly List<(long tempSeq, EditBatch batch)> _pending = new List<(long, EditBatch)>();

        /// <summary>Our own user id — the tie-break for concurrent inserts at the
        /// identical offset (contract §5).</summary>
        public string SelfId = "";

        public CoEditDocument(string path) => Path = path;

        public long BaseSeq => _serverSeq;
        public bool HasPending => _pending.Count > 0;

        /// <summary>The document just became shared: `content` is the promoted
        /// base and `baseSeq` the sequence it corresponds to.</summary>
        public void Reset(long baseSeq, string content, bool readOnly)
        {
            _serverText = content ?? "";
            _serverSeq = baseSeq;
            _history.Clear();
            _pending.Clear();
            ReadOnly = readOnly;
        }

        // --------------------------------------------------------- local edits

        /// <summary>Record a local edit batch. Returns the wire JSON to send, or
        /// null when there is nothing to send.</summary>
        public string RecordLocal(EditBatch batch, long tempSeq)
        {
            if (batch == null || batch.IsEmpty || ReadOnly)
                return null;
            batch.Doc = Path;
            batch.Base = _serverSeq;
            _pending.Add((tempSeq, batch));
            return CoEditMerge.ToJson(batch);
        }

        /// <summary>Our batch was sequenced. The head of `pending` has already
        /// been transformed past every remote op that arrived while it was in
        /// flight, so it IS its own server-space form: fold it into serverText
        /// and retire it.</summary>
        public void Acknowledged(long tempSeq, long seq)
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].tempSeq != tempSeq)
                    continue;
                var batch = _pending[i].batch;
                _pending.RemoveAt(i);
                _serverText = CoEditMerge.Apply(_serverText, batch);
                _serverSeq = seq;
                Remember(seq, batch);
                return;
            }
            // Unknown correlation id: the ack outlived a reset. Keep the sequence
            // moving so later transforms still have a sane base.
            if (seq > _serverSeq)
                _serverSeq = seq;
        }

        /// <summary>ALS assigns the correlation id its acks will carry, so the
        /// pending entry is re-keyed from ATE's provisional id to that one.</summary>
        public void Reindex(long fromTempSeq, long toTempSeq)
        {
            for (var i = 0; i < _pending.Count; i++)
                if (_pending[i].tempSeq == fromTempSeq)
                {
                    _pending[i] = (toTempSeq, _pending[i].batch);
                    return;
                }
        }

        // -------------------------------------------------------- remote edits

        /// <summary>Lift a remote batch into the user's coordinate space and
        /// return the edits to apply to the visible buffer. Also advances
        /// serverText and rebases our pending edits. Returns null if the batch is
        /// unusable.</summary>
        public List<Edit> IncorporateRemote(long seq, string authorId, string batchJson)
        {
            var incoming = CoEditMerge.FromJson(batchJson);
            if (incoming == null)
                return null;

            // 1. Lift into our server space: transform past everything sequenced
            //    after the author's base, which is precisely what they hadn't seen.
            var lifted = incoming;
            foreach (var (hseq, hbatch) in _history)
                if (hseq > incoming.Base && hseq < seq)
                    lifted = CoEditMerge.Transform(lifted, hbatch, aFirst: false);

            _serverText = CoEditMerge.Apply(_serverText, lifted);
            _serverSeq = seq;
            Remember(seq, lifted);

            // 2. Lift into the user's space: our pending edits are not sequenced
            //    yet, so the remote batch precedes them and wins the tie.
            var forBuffer = lifted;
            var selfFirst = CoEditMerge.AuthorWins(authorId, SelfId);
            for (var i = 0; i < _pending.Count; i++)
            {
                var mine = _pending[i].batch;
                var rebased = CoEditMerge.Transform(mine, forBuffer, aFirst: !selfFirst);
                forBuffer = CoEditMerge.Transform(forBuffer, mine, aFirst: selfFirst);
                _pending[i] = (_pending[i].tempSeq, rebased);
            }
            return forBuffer.Edits;
        }

        void Remember(long seq, EditBatch serverSpace)
        {
            _history.Add((seq, serverSpace));
            if (_history.Count > HistoryCap)
                _history.RemoveRange(0, _history.Count - HistoryCap);
        }

        // ------------------------------------------------------------ undo help

        /// <summary>Rebase a local undo entry's offsets past a remote edit. ATE's
        /// undo is delta-shaped, so an undo IS an ordinary edit and can be moved
        /// like one — without this, undoing after a peer typed above you would
        /// splice at a stale offset and corrupt the document (contract §8).</summary>
        public static Edit RebaseUndo(Edit undoEntry, Edit remote) =>
            CoEditMerge.Transform(undoEntry, remote, aFirst: false);
    }
}
