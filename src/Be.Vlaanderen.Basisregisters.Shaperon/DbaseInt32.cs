namespace Be.Vlaanderen.Basisregisters.Shaperon
{
    using System;
    using System.Globalization;
    using System.IO;

    public class DbaseInt32 : DbaseFieldValue
    {
        public static readonly DbaseIntegerDigits MaximumIntegerDigits = new DbaseIntegerDigits(10);

        private int? _value;

        public DbaseInt32(DbaseField field) : base(field)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));

            if (field.FieldType != DbaseFieldType.Number && field.FieldType != DbaseFieldType.Float)
                throw new ArgumentException(
                    $"The field {field.Name}'s type must be either number or float to use it as a long integer field.",
                    nameof(field));

            if (field.DecimalCount.ToInt32() != 0)
                throw new ArgumentException(
                    $"The number field {field.Name}'s decimal count must be 0 to use it as a long integer field.",
                    nameof(field));

            _value = null;
        }

        public DbaseInt32(DbaseField field, int value) : base(field)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));

            if (field.FieldType != DbaseFieldType.Number && field.FieldType != DbaseFieldType.Float)
                throw new ArgumentException(
                    $"The field {field.Name}'s type must be either number or float to use it as a long integer field.",
                    nameof(field));

            if (field.DecimalCount.ToInt32() != 0)
                throw new ArgumentException(
                    $"The number field {field.Name}'s decimal count must be 0 to use it as a long integer field.",
                    nameof(field));

            Value = value;
        }

        public bool AcceptsValue(int value)
        {
            return FormatAsString(value).Length <= Field.Length.ToInt32();
        }

        public int Value
        {
            get
            {
                if (!_value.HasValue)
                {
                    throw new FormatException($"The field {Field.Name} can not be null when read as non nullable datatype.");
                }
                return _value.Value;
            }
            set
            {
                var length = FormatAsString(value).Length;

                if (length > Field.Length.ToInt32())
                    throw new FormatException(
                        $"The value length {length} of field {Field.Name} is greater than its field length {Field.Length}.");

                _value = value;
            }
        }

        private static string FormatAsString(int value) => value.ToString(CultureInfo.InvariantCulture);

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            if (reader.PeekChar() == '\0')
            {
                var read = reader.ReadBytes(Field.Length.ToInt32());
                if (read.Length != Field.Length.ToInt32())
                {
                    throw new EndOfStreamException(
                        $"Unable to read beyond the end of the stream. Expected stream to have {Field.Length.ToInt32()} byte(s) available but only found {read.Length} byte(s) as part of reading field {Field.Name.ToString()}."
                    );
                }
                _value = null;
            }
            else
            {
                var unpadded = reader.ReadLeftPaddedString(Field.Name.ToString(), Field.Length.ToInt32(), ' ');

                _value = int.TryParse(unpadded,
                    NumberStyles.Integer | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                    CultureInfo.InvariantCulture, out var parsed)
                    ? (int?) parsed
                    : null;
            }
        }

        public override void Write(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (_value.HasValue)
            {
                var unpadded = FormatAsString(_value.Value);
                writer.WriteLeftPaddedString(unpadded, Field.Length.ToInt32(), ' ');
            }
            else
            {
                writer.Write(new string(' ', Field.Length.ToInt32()).ToCharArray());
                // or writer.Write(new byte[Field.Length]); // to determine
            }
        }

        public override void Accept(IDbaseFieldValueVisitor visitor) => (visitor as ITypedDbaseFieldValueVisitor)?.Visit(this);
    }
}