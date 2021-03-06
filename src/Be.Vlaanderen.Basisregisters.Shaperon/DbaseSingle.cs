using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Be.Vlaanderen.Basisregisters.Shaperon
{
    public class DbaseSingle : DbaseFieldValue
    {
        // REMARK: Actual max single integer digits is 38, but float dbase type only supports 20.
        public static readonly DbaseIntegerDigits MaximumIntegerDigits = new DbaseIntegerDigits(20);
        public static readonly DbaseFieldLength MaximumLength = new DbaseFieldLength(20);
        public static readonly DbaseFieldLength PositiveValueMinimumLength = new DbaseFieldLength(3); // 0.0
        public static readonly DbaseFieldLength NegativeValueMinimumLength = new DbaseFieldLength(4); // -0.0

        public static readonly DbaseFieldLength MinimumLength =
            DbaseFieldLength.Min(PositiveValueMinimumLength, NegativeValueMinimumLength);

        public static readonly DbaseDecimalCount MaximumDecimalCount = new DbaseDecimalCount(7);

        private const NumberStyles NumberStyle = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
                                                 NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;

        private NumberFormatInfo Provider { get; }

        private float? _value;

        public DbaseSingle(DbaseField field, float? value = null) : base(field)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (field.FieldType != DbaseFieldType.Float)
            {
                throw new ArgumentException(
                    $"The field {field.Name} 's type must be float to use it as a single field.", nameof(field));
            }

            if (field.Length < MinimumLength || field.Length > MaximumLength)
            {
                throw new ArgumentException(
                    $"The field {field.Name} 's length ({field.Length}) must be between {MinimumLength} and {MaximumLength}.",
                    nameof(field));
            }

            Provider = new NumberFormatInfo
            {
                NumberDecimalDigits = DbaseDecimalCount.Min(MaximumDecimalCount, field.DecimalCount).ToInt32(),
                NumberDecimalSeparator = "."
            };

            Value = value;
        }

        public bool AcceptsValue(float? value)
        {
            if (value.HasValue)
            {
                if (Field.DecimalCount.ToInt32() == 0)
                {
                    return ((float) Math.Truncate(value.Value)).ToString("F", Provider).Length <=
                           Field.Length.ToInt32();
                }

                var digits = DbaseDecimalCount.Min(MaximumDecimalCount, Field.DecimalCount).ToInt32();
                var rounded = (float) Math.Round(value.Value, digits);
                return rounded.ToString("F", Provider).Length <= Field.Length.ToInt32();
            }

            return true;
        }

        public float? Value
        {
            get => _value;
            set
            {
                if (value.HasValue)
                {
                    // if(Single.IsNaN(value.Value))
                    // {
                    //     throw new ArgumentException($"The value of field {Field.Name} can not be not-a-number (NaN).");
                    // }

                    // if(Single.IsNegativeInfinity(value.Value))
                    // {
                    //     throw new ArgumentException($"The value of field {Field.Name} can not be negative infinite.");
                    // }

                    // if(Single.IsPositiveInfinity(value.Value))
                    // {
                    //     throw new ArgumentException($"The value of field {Field.Name} can not be positive infinite.");
                    // }

                    if (Field.DecimalCount.ToInt32() == 0)
                    {
                        var truncated = (float) Math.Truncate(value.Value);
                        var length = truncated.ToString("F", Provider).Length;
                        if (length > Field.Length.ToInt32())
                        {
                            throw new ArgumentException(
                                $"The length ({length}) of the value ({truncated}) of field {Field.Name} is greater than its field length {Field.Length}, which would result in loss of precision.");
                        }

                        _value = truncated;
                    }
                    else
                    {
                        var digits = DbaseDecimalCount.Min(MaximumDecimalCount, Field.DecimalCount).ToInt32();
                        var rounded = (float) Math.Round(value.Value, digits);
                        var roundedFormatted = rounded.ToString("F", Provider);
                        var length = roundedFormatted.Length;
                        if (length > Field.Length.ToInt32())
                        {
                            throw new ArgumentException(
                                $"The length ({length}) of the value ({roundedFormatted}) of field {Field.Name} is greater than its field length {Field.Length}, which would result in loss of precision.");
                        }

                        _value = Single.Parse(roundedFormatted,
                            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Provider);
                    }
                }
                else
                {
                    _value = null;
                }
            }
        }

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (reader.PeekChar() == '\0')
            {
                reader.ReadBytes(Field.Length.ToInt32());
                Value = null;
            }
            else
            {
                var unpadded = reader.ReadLeftPaddedString(Field.Length.ToInt32(), ' ');
                if (Single.TryParse(unpadded, NumberStyle, Provider, out var parsed))
                {
                    Value = parsed;
                }
                else
                {
                    Value = null;
                }
            }
        }

        public override void Write(BinaryWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (Value.HasValue)
            {
                var unpadded = Value.Value.ToString("F", Provider);
                if (unpadded.Length < Field.Length.ToInt32() && Field.DecimalCount.ToInt32() > 0)
                {
                    // Pad with decimal zeros if space left.
                    var parts = unpadded.Split(Provider.NumberDecimalSeparator.Single());
                    if (parts.Length == 2 && parts[1].Length < Field.DecimalCount.ToInt32())
                    {
                        unpadded = String.Concat(
                            unpadded,
                            new string(
                                '0',
                                Field.DecimalCount.ToInt32() - parts[1].Length
                            )
                        );
                    }
                }

                writer.WriteLeftPaddedString(unpadded, Field.Length.ToInt32(), ' ');
            }
            else
            {
                writer.Write(new string(' ', Field.Length.ToInt32()).ToCharArray());
                // or writer.Write(new byte[Field.Length]); // to determine
            }
        }

        public override void Inspect(IDbaseFieldValueInspector writer)
        {
            writer.Inspect(this);
        }
    }
}
