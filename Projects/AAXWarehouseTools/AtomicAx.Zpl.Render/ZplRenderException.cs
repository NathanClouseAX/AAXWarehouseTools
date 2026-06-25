using System;

namespace AtomicAx.Zpl.Render
{
    /// <summary>
    /// Base exception for all failures originating in the ZPL rendering library.
    /// Carries the original analyzer/drawer message and (optionally) the inner cause.
    /// </summary>
    public class ZplRenderException : Exception
    {
        public ZplRenderException(string message)
            : base(message)
        {
        }

        public ZplRenderException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Thrown when the render call cannot determine the label dimensions:
    /// either the print density (dpmm) was not supplied, or width/height were not
    /// supplied and could not be parsed from the ZPL (^PW / ^LL). The X++
    /// caller owns the fallback to the WHS parameter defaults.
    /// </summary>
    public sealed class ZplDimensionsMissingException : ZplRenderException
    {
        public ZplDimensionsMissingException(string message)
            : base(message)
        {
        }

        public ZplDimensionsMissingException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
