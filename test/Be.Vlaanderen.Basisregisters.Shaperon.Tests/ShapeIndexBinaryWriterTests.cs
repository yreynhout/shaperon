namespace Be.Vlaanderen.Basisregisters.Shaperon
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Albedo;
    using AutoFixture;
    using AutoFixture.Idioms;
    using GeoAPI.Geometries;
    using NetTopologySuite.Geometries;
    using Xunit;

    public class ShapeIndexBinaryWriterTests
    {
        private readonly Fixture _fixture;

        public ShapeIndexBinaryWriterTests()
        {
            _fixture = new Fixture();
            _fixture.CustomizeShapeRecordCount(100);
            _fixture.CustomizeRecordNumber();
            _fixture.CustomizeWordLength();
            _fixture.CustomizeWordOffset();
            _fixture.Customize<ShapeType>(customization =>
                customization.FromFactory(generator =>
                    {
                        var result = ShapeType.NullShape;
                        switch (generator.Next() % 3)
                        {
                            //case 0:
                            case 1:
                                result = ShapeType.Point;
                                break;
                            case 2:
                                result = ShapeType.PolyLineM;
                                break;
                        }

                        return result;
                    }
                ).OmitAutoProperties()
            );
            _fixture.Customize<PointM>(customization =>
                customization.FromFactory(generator =>
                    new PointM(
                        _fixture.Create<double>(),
                        _fixture.Create<double>(),
                        _fixture.Create<double>(),
                        _fixture.Create<double>()
                    )
                ).OmitAutoProperties()
            );
            _fixture.Customize<ILineString>(customization =>
                customization.FromFactory(generator =>
                    new LineString(
                        new PointSequence(_fixture.CreateMany<PointM>()),
                        GeometryConfiguration.GeometryFactory)
                ).OmitAutoProperties()
            );
            _fixture.Customize<MultiLineString>(customization =>
                customization.FromFactory(generator =>
                    new MultiLineString(_fixture.CreateMany<ILineString>(generator.Next(1, 10)).ToArray())
                ).OmitAutoProperties()
            );
            _fixture.Customize<ShapeContent>(customization =>
                customization.FromFactory(generator =>
                {
                    ShapeContent content = null;
                    switch (generator.Next() % 3)
                    {
                        case 0:
                            content = NullShapeContent.Instance;
                            break;
                        case 1:
                            content = new PointShapeContent(_fixture.Create<PointM>());
                            break;
                        case 2:
                            content = new PolyLineMShapeContent(_fixture.Create<MultiLineString>());
                            break;
                    }

                    return content;
                }));
            _fixture.Register<Stream>(() => new MemoryStream());
        }

        [Fact]
        public void HeaderOrWriterCanNotBeNull()
        {
            new GuardClauseAssertion(_fixture)
                .Verify(Constructors.Select(() => new ShapeIndexBinaryWriter(null, null)));
        }

        [Fact]
        public void PropertiesReturnExpectedValues()
        {
            var header = _fixture.Create<ShapeFileHeader>();
            using (var writer = new BinaryWriter(new MemoryStream()))
            using (var sut = new ShapeIndexBinaryWriter(header, writer))
            {
                Assert.Same(header, sut.Header);
                Assert.Same(writer, sut.Writer);
            }
        }

        [Fact]
        public void WriteOneHasExpectedResult()
        {
            var shapeType = _fixture.Create<ShapeType>();
            var recordCount = _fixture.Create<ShapeRecordCount>();
            var shapeRecord = GenerateOneRecord(shapeType);
            var length = ShapeFileHeader.Length.Plus(shapeRecord.Length);
            var boundingBox = BoundingBox3D.Empty;
            if (shapeRecord.Content is PointShapeContent pointContent)
                boundingBox = BoundingBox3D.FromGeometry(pointContent.Shape);
            if (shapeRecord.Content is PolyLineMShapeContent lineContent)
                boundingBox = BoundingBox3D.FromGeometry(lineContent.Shape);
            var expectedHeader = new ShapeFileHeader(length, shapeType, boundingBox).ForIndex(recordCount);
            var expectedRecord = shapeRecord.IndexAt(ShapeIndexRecord.InitialOffset);
            using (var stream = new MemoryStream())
            {
                using (var sut =
                    new ShapeIndexBinaryWriter(expectedHeader, new BinaryWriter(stream, Encoding.ASCII, true)))
                {
                    //Act
                    sut.Write(expectedRecord);
                }

                // Assert
                stream.Position = 0;

                using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
                {
                    var actualHeader = ShapeFileHeader.Read(reader);
                    var actualRecord = ShapeIndexRecord.Read(reader);

                    Assert.Equal(expectedHeader, actualHeader);
                    Assert.Equal(expectedRecord, actualRecord, new ShapeIndexRecordEqualityComparer());
                }
            }
        }

        [Fact]
        public void WriteManyHasExpectedResult()
        {
            var shapeType = _fixture.Create<ShapeType>();
            var recordCount = _fixture.Create<ShapeRecordCount>();
            var shapeRecords = GenerateManyRecords(shapeType, recordCount);
            var length = shapeRecords.Aggregate(ShapeFileHeader.Length,
                (result, current) => result.Plus(current.Length));
            var boundingBox = shapeRecords.Aggregate(BoundingBox3D.Empty,
                (result, current) =>
                {
                    if (current.Content is PointShapeContent pointContent)
                        return result.ExpandWith(BoundingBox3D.FromGeometry(pointContent.Shape));
                    if (current.Content is PolyLineMShapeContent lineContent)
                        return result.ExpandWith(BoundingBox3D.FromGeometry(lineContent.Shape));
                    return result;
                });
            var expectedHeader = new ShapeFileHeader(length, shapeType, boundingBox);
            var expectedRecords = new ShapeIndexRecord[shapeRecords.Length];
            var offset = ShapeIndexRecord.InitialOffset;
            for (var index = 0; index < shapeRecords.Length; index++)
            {
                var shapeRecord = shapeRecords[index];
                expectedRecords[index] = shapeRecord.IndexAt(offset);
                offset = offset.Plus(shapeRecord.Length);
            }

            using (var stream = new MemoryStream())
            {
                using (var sut =
                    new ShapeIndexBinaryWriter(expectedHeader, new BinaryWriter(stream, Encoding.ASCII, true)))
                {
                    //Act
                    sut.Write(expectedRecords);
                }

                // Assert
                stream.Position = 0;

                using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
                {
                    var actualHeader = ShapeFileHeader.Read(reader);
                    var actualRecords = new List<ShapeIndexRecord>();
                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        actualRecords.Add(ShapeIndexRecord.Read(reader));
                    }

                    Assert.Equal(expectedHeader, actualHeader);
                    Assert.Equal(expectedRecords, actualRecords, new ShapeIndexRecordEqualityComparer());
                }
            }
        }

        private ShapeRecord GenerateOneRecord(ShapeType typeOfShape)
        {
            ShapeContent content = null;
            switch (typeOfShape)
            {
                case ShapeType.Point:
                    content = new PointShapeContent(_fixture.Create<PointM>());
                    break;
                case ShapeType.PolyLineM:
                    content = new PolyLineMShapeContent(_fixture.Create<MultiLineString>());
                    break;
                //case ShapeType.NullShape:
                default:
                    content = NullShapeContent.Instance;
                    break;
            }

            return content.RecordAs(RecordNumber.Initial);
        }

        private ShapeRecord[] GenerateManyRecords(ShapeType typeOfShape, ShapeRecordCount count)
        {
            var records = new ShapeRecord[count.ToInt32()];
            var number = RecordNumber.Initial;
            for (var index = 0; index < records.Length; index++)
            {
                ShapeContent content = null;
                switch (typeOfShape)
                {
                    case ShapeType.Point:
                        content = new PointShapeContent(_fixture.Create<PointM>());
                        break;
                    case ShapeType.PolyLineM:
                        content = new PolyLineMShapeContent(_fixture.Create<MultiLineString>());
                        break;
                    //case ShapeType.NullShape:
                    default:
                        content = NullShapeContent.Instance;
                        break;
                }

                records[index] = content.RecordAs(number);
                number = number.Next();
            }

            return records;
        }
    }
}
