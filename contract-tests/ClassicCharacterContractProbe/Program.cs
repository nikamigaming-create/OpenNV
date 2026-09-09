using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

foreach (var (campaign, shift) in new[] { ("fallout-1", 0), ("fallout-2", 1) })
{
    var bytes = new byte[428 + shift * 4];
    void Word(int index, int value) => BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(index * 4), value);
    for (var index = 1; index <= 7; index++) Word(index, index == 1 ? 10 : 5);
    Word(34, 23); Word(35, 1); Word(71, 17); Word(88, 29);
    Encoding.ASCII.GetBytes("TEST").CopyTo(bytes, 368 + shift * 4);
    Word(100 + shift, 2); Word(101 + shift, 6); Word(102 + shift, 17); Word(103 + shift, -1);
    Word(104 + shift, 15); Word(105 + shift, -1);
    var character = ClassicPremadeReader.Read(campaign, bytes);
    if (character.Name != "TEST" || !character.Female || character.Age != 23 || character.Special[0] != 10 ||
        !character.TaggedSkills.SequenceEqual(new[] { 2, 6, 17 }) || !character.Traits.SequenceEqual(new[] { 15 }) ||
        character.SkillBonuses[0] != 17 || character.SkillBonuses[^1] != 29)
        throw new InvalidOperationException("GCD field decoding changed.");
    Reject(() => ClassicPremadeReader.Read(campaign, bytes.AsSpan(0, bytes.Length - 1)));
    Reject(() => ClassicPremadeReader.Read(shift == 0 ? "fallout-2" : "fallout-1", bytes));
    Word(101 + shift, 2); Reject(() => ClassicPremadeReader.Read(campaign, bytes)); Word(101 + shift, 6);
    Word(35, 2); Reject(() => ClassicPremadeReader.Read(campaign, bytes)); Word(35, 1);
    Word(104 + shift, 16); Reject(() => ClassicPremadeReader.Read(campaign, bytes)); Word(104 + shift, 15);
    Word(1, 11); Reject(() => ClassicPremadeReader.Read(campaign, bytes)); Word(1, 10);
    Array.Fill(bytes, (byte)'X', 368 + shift * 4, 32); Reject(() => ClassicPremadeReader.Read(campaign, bytes));
}
Console.WriteLine("OPENNV_CLASSIC_CHARACTER_CONTRACT_PASS campaignLayouts=2 malformedInputs=14");
ClassicTextLayoutContracts.Run();

static void Reject(Action action)
{
    try { action(); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Malformed character input was accepted.");
}
