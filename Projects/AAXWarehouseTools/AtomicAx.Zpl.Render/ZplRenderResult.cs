using System;
using System.Collections.Generic;

namespace AtomicAx.Zpl.Render
{
    /// <summary>
    /// Wraps the rendered PNG list for X++, which cannot declare a closed generic CLR type such as
    /// IList&lt;byte[]&gt;, exposing <see cref="Count"/> and <see cref="GetPng(int)"/> instead.
    /// </summary>
    public sealed class ZplRenderResult
    {
        private readonly IList<byte[]> _pngs;

        /// <summary>
        /// Initializes a new instance of the <see cref="ZplRenderResult"/> class.
        /// </summary>
        /// <param name="pngs">The rendered PNG images; null is treated as an empty list.</param>
        internal ZplRenderResult(IList<byte[]> pngs)
        {
            _pngs = pngs ?? new List<byte[]>();
        }

        /// <summary>
        /// Gets the number of PNG images produced, one per ^XA…^XZ label block.
        /// </summary>
        public int Count
        {
            get { return _pngs.Count; }
        }

        /// <summary>
        /// Returns the PNG bytes for the label block at the given index.
        /// </summary>
        /// <param name="index">The zero-based index of the label block.</param>
        /// <returns>The PNG bytes of the label block.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The index is outside the rendered range.</exception>
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
