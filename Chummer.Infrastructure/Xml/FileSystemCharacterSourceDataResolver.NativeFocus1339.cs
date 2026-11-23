using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    // This is the immutable capture decision, independent of public request flags.
    // The shared wallet still requires its original private execution evidence.
    bool ICharacterSourceDataResolver.UsesNativeFocusCapture1339
        => _retainedInputs?.NativeFocusGearCatalog1339 == true;
}
