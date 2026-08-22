using AnimeOverseer.Server.Models;
using AnimeOverseer.Server.Services;
using Xunit;

namespace AnimeOverseer.Server.Tests;

public sealed class NestedFilterGroupTests
{
    [Fact]
    public void Matches_an_and_group_with_an_or_genre_subgroup()
    {
        var state = new FilterState
        {
            Groups = [new FilterGroup
            {
                Logic = GroupLogic.And,
                Conditions = [new FilterCondition { Field = FilterField.Season, Operator = FilterOperator.Equals, Value = "fall" }],
                Subgroups = [new FilterGroup
                {
                    Logic = GroupLogic.Or,
                    Conditions = [
                        new FilterCondition { Field = FilterField.Genres, Operator = FilterOperator.Equals, Value = "Isekai" },
                        new FilterCondition { Field = FilterField.Genres, Operator = FilterOperator.Equals, Value = "Fantasy" }
                    ]
                }]
            }]
        };
        var matching = new Anime { Title = "Match", Season = new Season { Name = "fall" }, AnimeGenres = [new AnimeGenre { Genre = new Genre { Name = "Fantasy" } }] };
        var wrongSeason = new Anime { Title = "Wrong season", Season = new Season { Name = "summer" }, AnimeGenres = [new AnimeGenre { Genre = new Genre { Name = "Isekai" } }] };
        var wrongGenre = new Anime { Title = "Wrong genre", Season = new Season { Name = "fall" }, AnimeGenres = [new AnimeGenre { Genre = new Genre { Name = "Drama" } }] };

        var result = FilterEngine.Apply([matching, wrongSeason, wrongGenre], state);

        Assert.Equal([matching], result);
    }
}
