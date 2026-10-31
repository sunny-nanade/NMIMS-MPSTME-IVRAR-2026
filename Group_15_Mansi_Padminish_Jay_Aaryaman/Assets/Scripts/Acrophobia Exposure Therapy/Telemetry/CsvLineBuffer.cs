using System;
using System.IO;

namespace Acrophobia.Telemetry
{
    /// <summary>
    /// Builds CSV lines into a reusable char buffer without allocating.
    ///
    /// Every mainstream route from a float to text in .NET allocates a string:
    /// float.ToString(), string.Format, interpolation, and StringBuilder.Append(float)
    /// all do. On a 72 FPS device that GC churn is the difference between a telemetry
    /// system you can leave running and one you have to switch off to hit frame time.
    ///
    /// So numbers are formatted here by integer arithmetic straight into a char[], and
    /// handed to StreamWriter.Write(char[], int, int), which is the one write overload
    /// that does not allocate. The only allocation in this class happens in the
    /// constructor.
    ///
    /// Not thread safe, and intentionally so - it is owned by one logger on the main thread.
    /// </summary>
    public sealed class CsvLineBuffer
    {
        private const int DigitScratchSize = 24;

        private readonly char[] _buffer;
        private readonly char[] _digitScratch = new char[DigitScratchSize];
        private int _length;
        private bool _fieldPending;

        public CsvLineBuffer(int capacity = 1024)
        {
            if (capacity < 64)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Need room for at least one row.");
            }

            _buffer = new char[capacity];
            _length = 0;
            _fieldPending = false;
        }

        public int Length => _length;

        public int Capacity => _buffer.Length;

        public void Clear()
        {
            _length = 0;
            _fieldPending = false;
        }

        /// <summary>
        /// Writes a comma before the next field unless it is first on the line.
        /// </summary>
        private void Separate()
        {
            if (_fieldPending)
            {
                Push(',');
            }

            _fieldPending = true;
        }

        private void Push(char c)
        {
            if (_length >= _buffer.Length)
            {
                // Dropping the tail is preferable to either throwing mid-session or
                // growing (which would allocate). Capacity is sized generously by the
                // caller; hitting this means a row definition grew unexpectedly.
                return;
            }

            _buffer[_length++] = c;
        }

        public void AppendField(long value)
        {
            Separate();
            WriteInteger(value);
        }

        public void AppendField(int value)
        {
            AppendField((long)value);
        }

        public void AppendField(bool value)
        {
            Separate();
            Push(value ? '1' : '0');
        }

        /// <summary>
        /// Appends a float with a fixed number of decimal places, rounded half-away-from-zero.
        /// NaN and the infinities are emitted as empty fields so a spreadsheet or pandas
        /// reads them as missing data rather than choking on a token.
        /// </summary>
        public void AppendField(float value, int decimals = 4)
        {
            Separate();

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return;
            }

            if (decimals < 0)
            {
                decimals = 0;
            }
            else if (decimals > 9)
            {
                decimals = 9;
            }

            bool negative = value < 0f;
            double magnitude = negative ? -(double)value : value;

            long scale = 1;
            for (int i = 0; i < decimals; i++)
            {
                scale *= 10;
            }

            // Round to the requested precision before splitting, so 0.99996 at 4dp
            // carries into the integer part instead of printing "0.0000".
            double scaled = magnitude * scale + 0.5;
            if (scaled >= 9.2233720368547758E+18)
            {
                // Beyond long range. Nothing in metres-scale VR telemetry reaches here,
                // but emit empty rather than wrap into a nonsense value.
                return;
            }

            long totalUnits = (long)scaled;
            long whole = totalUnits / scale;
            long fraction = totalUnits - whole * scale;

            if (negative && (whole != 0 || fraction != 0))
            {
                Push('-');
            }

            WriteUnsignedInteger(whole);

            if (decimals == 0)
            {
                return;
            }

            Push('.');

            // Left-pad the fractional part so 0.05 does not print as "0.5".
            long divisor = scale / 10;
            while (divisor > 0)
            {
                long digit = fraction / divisor;
                Push((char)('0' + (int)digit));
                fraction -= digit * divisor;
                divisor /= 10;
            }
        }

        /// <summary>
        /// Appends text. Allocation-free in itself, but callers should only pass literals,
        /// cached strings or interned enum names - never a freshly built string, or the
        /// allocation simply moves to the call site.
        ///
        /// Commas, quotes and newlines are escaped per RFC 4180.
        /// </summary>
        public void AppendField(string value)
        {
            Separate();

            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            bool needsQuotes = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == ',' || c == '"' || c == '\n' || c == '\r')
                {
                    needsQuotes = true;
                    break;
                }
            }

            if (!needsQuotes)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    Push(value[i]);
                }

                return;
            }

            Push('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"')
                {
                    Push('"');
                }

                Push(c);
            }

            Push('"');
        }

        public void EndLine()
        {
            Push('\n');
            _fieldPending = false;
        }

        /// <summary>
        /// Flushes the buffered characters to the writer and resets. The char[] overload
        /// is used deliberately: StreamWriter.Write(string) would require building one.
        /// </summary>
        public void WriteTo(StreamWriter writer)
        {
            if (writer == null || _length == 0)
            {
                Clear();
                return;
            }

            writer.Write(_buffer, 0, _length);
            Clear();
        }

        private void WriteInteger(long value)
        {
            if (value < 0)
            {
                Push('-');
                // -long.MinValue overflows, so negate as unsigned.
                WriteUnsignedInteger((ulong)(-(value + 1)) + 1UL);
                return;
            }

            WriteUnsignedInteger((ulong)value);
        }

        private void WriteUnsignedInteger(long value)
        {
            WriteUnsignedInteger(value < 0 ? 0UL : (ulong)value);
        }

        private void WriteUnsignedInteger(ulong value)
        {
            if (value == 0)
            {
                Push('0');
                return;
            }

            int count = 0;
            while (value > 0 && count < DigitScratchSize)
            {
                _digitScratch[count++] = (char)('0' + (int)(value % 10UL));
                value /= 10UL;
            }

            for (int i = count - 1; i >= 0; i--)
            {
                Push(_digitScratch[i]);
            }
        }
    }
}
