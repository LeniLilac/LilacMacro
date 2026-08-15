using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal static class ExpeditionRewardPriorCatalog
{
    public const string Revision = "2026-08-15-post-update";

    private static readonly IReadOnlyDictionary<(int Difficulty, ExpeditionRewardResource Resource), int[]> Encoded =
        new Dictionary<(int, ExpeditionRewardResource), int[]>
        {
            [DifficultyResource(1, ExpeditionRewardResource.FuelCell)] =
            [
                0, 64, 5, 49, 7, 138, 8, 13, 10, 89, 12, 94, 13, 60, 14, 30, 15, 32,
                17, 107, 18, 6, 19, 34, 20, 65, 21, 8, 22, 60, 23, 16, 24, 31, 25, 8,
                26, 13, 27, 31, 28, 4, 29, 14, 30, 15, 31, 2, 32, 5, 33, 3, 34, 4,
                35, 6, 36, 2, 37, 4, 38, 2, 39, 2, 40, 1, 41, 3, 42, 1, 46, 1, 47, 2,
            ],
            [DifficultyResource(1, ExpeditionRewardResource.EquipmentScrap)] =
            [
                0, 116, 5, 165, 7, 40, 8, 69, 10, 12, 12, 154, 13, 17, 14, 12, 15, 21,
                16, 3, 17, 122, 18, 11, 19, 74, 20, 45, 21, 7, 22, 25, 23, 3, 24, 15,
                25, 18, 26, 11, 27, 14, 28, 7, 29, 14, 30, 7, 31, 1, 32, 11, 33, 3,
                34, 9, 35, 1, 36, 4, 37, 1, 38, 3, 42, 1, 44, 1, 46, 1, 51, 1,
            ],
            [DifficultyResource(1, ExpeditionRewardResource.EquipmentReroll)] =
            [0, 406, 1, 221, 2, 70, 3, 293, 4, 20, 5, 4, 6, 3, 7, 2],
            [DifficultyResource(1, ExpeditionRewardResource.EquipmentLock)] =
            [0, 370, 1, 245, 2, 60, 3, 290, 4, 26, 5, 6, 6, 21, 7, 1],
            [DifficultyResource(1, ExpeditionRewardResource.ExpeditionCoin)] =
            [
                0, 77, 5, 83, 7, 138, 8, 28, 10, 91, 12, 127, 13, 33, 14, 17, 15, 25,
                16, 1, 17, 90, 18, 11, 19, 19, 20, 72, 21, 7, 22, 58, 23, 6, 24, 17,
                25, 12, 26, 10, 27, 24, 28, 6, 29, 15, 30, 9, 31, 3, 32, 11, 33, 2,
                34, 3, 35, 1, 36, 5, 37, 1, 38, 1, 39, 1, 40, 2, 41, 4, 43, 3, 46, 1,
                47, 3, 51, 1, 68, 1,
            ],
            [DifficultyResource(2, ExpeditionRewardResource.FuelCell)] =
            [
                0, 71, 5, 54, 7, 87, 10, 81, 12, 38, 13, 60, 14, 44, 15, 32, 16, 1,
                17, 100, 18, 4, 19, 25, 20, 70, 21, 10, 22, 67, 23, 22, 24, 37, 25, 8,
                26, 12, 27, 30, 28, 7, 29, 20, 30, 20, 31, 6, 32, 20, 33, 11, 34, 7,
                35, 8, 36, 6, 37, 9, 38, 8, 39, 7, 41, 3, 42, 1, 43, 4, 44, 2, 46, 2,
                47, 2, 52, 1, 53, 1, 57, 1, 58, 1,
            ],
            [DifficultyResource(2, ExpeditionRewardResource.EquipmentScrap)] =
            [
                0, 82, 5, 130, 8, 96, 10, 12, 12, 128, 13, 20, 14, 11, 15, 19, 16, 2,
                17, 95, 18, 8, 19, 78, 20, 44, 21, 8, 22, 31, 23, 8, 24, 29, 25, 21,
                26, 25, 27, 18, 28, 19, 29, 25, 30, 10, 31, 1, 32, 25, 33, 6, 34, 14,
                36, 10, 37, 7, 38, 4, 39, 2, 41, 1, 42, 1, 43, 1, 47, 2, 48, 1, 49, 1,
                50, 1, 51, 3, 65, 1,
            ],
            [DifficultyResource(2, ExpeditionRewardResource.EquipmentReroll)] =
            [0, 404, 1, 219, 2, 70, 3, 291, 4, 5, 5, 4, 6, 4, 7, 3],
            [DifficultyResource(2, ExpeditionRewardResource.EquipmentLock)] =
            [0, 364, 1, 242, 2, 59, 3, 146, 4, 97, 5, 14, 6, 50, 7, 23, 8, 1, 9, 3, 10, 1],
            [DifficultyResource(2, ExpeditionRewardResource.ExpeditionCoin)] =
            [
                0, 63, 5, 68, 7, 86, 10, 88, 12, 88, 13, 55, 14, 22, 15, 30, 16, 1,
                17, 95, 18, 10, 19, 17, 20, 70, 21, 6, 22, 42, 23, 8, 24, 27, 25, 20,
                26, 8, 27, 38, 28, 7, 29, 19, 30, 28, 31, 6, 32, 18, 33, 3, 34, 12,
                35, 3, 36, 14, 37, 5, 38, 4, 39, 1, 40, 6, 41, 13, 42, 2, 43, 6, 44, 2,
                45, 2, 46, 1, 47, 1, 48, 1, 49, 1, 51, 1, 56, 1, 68, 1,
            ],
            [DifficultyResource(3, ExpeditionRewardResource.FuelCell)] =
            [
                0, 32, 5, 24, 7, 31, 8, 6, 10, 23, 12, 41, 13, 13, 14, 14, 15, 34,
                16, 1, 17, 36, 18, 4, 19, 24, 20, 20, 21, 10, 22, 31, 23, 11, 24, 27,
                25, 7, 26, 11, 27, 16, 28, 6, 29, 16, 30, 12, 31, 9, 32, 13, 33, 7,
                34, 8, 35, 8, 36, 4, 37, 7, 38, 7, 39, 5, 40, 1, 41, 4, 42, 2, 43, 4,
                44, 2, 46, 3, 47, 3, 48, 1, 52, 2, 53, 1, 54, 1, 57, 2, 58, 2, 64, 1,
            ],
            [DifficultyResource(3, ExpeditionRewardResource.EquipmentScrap)] =
            [
                0, 30, 5, 32, 7, 24, 8, 19, 10, 28, 12, 32, 13, 9, 14, 14, 15, 25,
                16, 3, 17, 40, 18, 9, 19, 31, 20, 20, 21, 8, 22, 27, 23, 4, 24, 19,
                25, 17, 26, 5, 27, 21, 28, 8, 29, 16, 30, 12, 31, 2, 32, 13, 33, 7,
                34, 15, 35, 1, 36, 6, 37, 5, 38, 4, 39, 8, 40, 2, 41, 2, 42, 4, 43, 2,
                44, 1, 46, 2, 47, 2, 48, 1, 49, 3, 50, 2, 51, 5, 52, 1, 55, 1, 56, 3,
                58, 1, 65, 1,
            ],
            [DifficultyResource(3, ExpeditionRewardResource.EquipmentReroll)] =
            [0, 180, 1, 98, 2, 31, 3, 133, 4, 48, 5, 16, 6, 25, 7, 11, 8, 2, 9, 2, 10, 1],
            [DifficultyResource(3, ExpeditionRewardResource.EquipmentLock)] =
            [0, 173, 1, 115, 2, 28, 3, 117, 4, 53, 5, 9, 6, 33, 7, 13, 8, 1, 9, 3, 10, 1, 11, 1],
            [DifficultyResource(3, ExpeditionRewardResource.ExpeditionCoin)] =
            [
                0, 25, 5, 27, 7, 19, 8, 14, 10, 34, 12, 32, 13, 14, 14, 13, 15, 27,
                16, 1, 17, 36, 18, 12, 19, 21, 20, 18, 21, 7, 22, 27, 23, 5, 24, 18,
                25, 16, 26, 14, 27, 19, 28, 11, 29, 15, 30, 12, 31, 8, 32, 16, 33, 2,
                34, 13, 35, 3, 36, 7, 37, 7, 38, 5, 39, 3, 40, 6, 41, 10, 42, 2, 43, 5,
                44, 2, 45, 2, 46, 1, 47, 3, 48, 1, 49, 1, 50, 1, 51, 3, 53, 1, 56, 1,
                57, 1, 59, 1, 62, 1, 64, 1, 68, 1, 73, 1, 86, 1,
            ],
        };

    public static int PoolCount(int difficulty)
    {
        ValidateDifficulty(difficulty);
        int[] counts = Enum.GetValues<ExpeditionRewardResource>()
            .Where(resource => resource != ExpeditionRewardResource.None)
            .Select(resource => Histogram(difficulty, resource).Values.Sum())
            .Distinct()
            .ToArray();
        return counts.Length == 1
            ? counts[0]
            : throw new InvalidDataException($"Bundled Expedition reward prior {Revision} is inconsistent.");
    }

    public static Dictionary<string, int> Histogram(
        int difficulty,
        ExpeditionRewardResource resource)
    {
        ValidateDifficulty(difficulty);
        if (resource == ExpeditionRewardResource.None) throw new ArgumentOutOfRangeException(nameof(resource));
        int[] encoded = Encoded[(difficulty, resource)];
        Dictionary<string, int> histogram = new(StringComparer.Ordinal);
        for (int index = 0; index < encoded.Length; index += 2)
        {
            histogram[encoded[index].ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                encoded[index + 1];
        }
        return histogram;
    }

    private static (int, ExpeditionRewardResource) DifficultyResource(
        int difficulty,
        ExpeditionRewardResource resource) => (difficulty, resource);

    private static void ValidateDifficulty(int difficulty)
    {
        if (difficulty is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(difficulty));
    }
}
