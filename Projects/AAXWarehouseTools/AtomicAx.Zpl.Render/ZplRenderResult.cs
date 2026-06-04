using System;
using System.Collections.Generic;

namespace AtomicAx.Zpl.Render
{
    /// <summary>
    /// X++-friendly wrapper over the rendered PNG list. X++ cannot declare a closed generic
    /// CLR type such as IList&lt;byte[]&gt;, so it consumes Count + GetPng(index) instead.
    /// Backed by the same list RenderToPngList returns.
    /// </summary>
    public sealed class ZplRenderResult
    {
        private readonly IList<byte[]> _pngs;

        internal ZplRenderResult(IList<byte[]> pngs)
        {
            _pngs = pngs ?? new List<byte[]>();
        }

        /// <summary>Number of PNG images produced (one per ^XA…^XZ label block).</summary>
        public int Count
        {
            get { return _pngs.Count; }
        }

        /// <summary>Returns the PNG bytes for the label block at the given zero-based index.</summary>
        public byte[] GetPng(int index)
        {
            if (index < 0 || index >= _pngs.Count)
            {
                throw new ArgumentOutOfRangeException("index");
            }

            return _pngs[index];
        }
    }
}
