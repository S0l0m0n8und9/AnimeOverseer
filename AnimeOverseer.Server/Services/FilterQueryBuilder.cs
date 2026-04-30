using System.Linq.Expressions;
using System.Reflection;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Services;

public static class FilterQueryBuilder
{
    public static IQueryable<Anime> Apply(IQueryable<Anime> query, FilterState state)
    {
        var activeGroups = state.Groups
            .Where(g => g.Conditions.Any(c => !string.IsNullOrEmpty(c.Value)))
            .ToList();

        if (activeGroups.Count == 0) return query;

        var param = Expression.Parameter(typeof(Anime), "a");

        var groupExprs = activeGroups
            .Select(g => BuildGroup(g, param))
            .OfType<Expression>()
            .ToList();

        if (groupExprs.Count == 0) return query;

        var body = groupExprs.Count == 1
            ? groupExprs[0]
            : groupExprs.Aggregate(state.TopLevelLogic == GroupLogic.And
                ? (Func<Expression, Expression, Expression>)Expression.AndAlso
                : Expression.OrElse);

        return query.Where(Expression.Lambda<Func<Anime, bool>>(body, param));
    }

    private static Expression? BuildGroup(FilterGroup group, ParameterExpression param)
    {
        var exprs = group.Conditions
            .Where(c => !string.IsNullOrEmpty(c.Value))
            .Select(c => BuildCondition(c, param))
            .OfType<Expression>()
            .ToList();

        if (exprs.Count == 0) return null;

        return exprs.Count == 1
            ? exprs[0]
            : exprs.Aggregate(group.Logic == GroupLogic.And
                ? (Func<Expression, Expression, Expression>)Expression.AndAlso
                : Expression.OrElse);
    }

    private static Expression? BuildCondition(FilterCondition c, ParameterExpression param)
    {
        var val = c.Value.Trim();
        if (string.IsNullOrEmpty(val)) return null;

        var season = Expression.Property(param, "Season");

        return c.Field switch
        {
            FilterField.Title        => TextExpr(Expression.Property(param, "Title"), c.Operator, val),
            FilterField.OriginalTitle => TextExpr(Expression.Property(param, "OriginalTitle"), c.Operator, val),
            FilterField.Type         => TextExpr(Expression.Property(param, "Type"), c.Operator, val),
            FilterField.Status       => TextExpr(Expression.Property(param, "Status"), c.Operator, val),
            FilterField.Season       => TextExpr(Expression.Property(season, "Name"), c.Operator, val),
            FilterField.Episodes     => NullableIntExpr(Expression.Property(param, "Episodes"), c.Operator, val),
            FilterField.Rating       => NullableDecimalExpr(Expression.Property(param, "Rating"), c.Operator, val),
            FilterField.Year         => IntExpr(Expression.Property(season, "Year"), c.Operator, val),
            FilterField.Genres       => TagExpr(param, "AnimeGenres", "Genre", c.Operator, val),
            FilterField.Themes       => TagExpr(param, "AnimeThemes", "Theme", c.Operator, val),
            FilterField.Demographics => TagExpr(param, "AnimeDemographics", "Demographic", c.Operator, val),
            _                        => null
        };
    }

    private static Expression TextExpr(Expression prop, FilterOperator op, string val)
    {
        var v = Expression.Constant(val);
        return op switch
        {
            FilterOperator.Contains    => StringCall(prop, "Contains", v),
            FilterOperator.NotContains => Expression.Not(StringCall(prop, "Contains", v)),
            FilterOperator.StartsWith  => StringCall(prop, "StartsWith", v),
            FilterOperator.EndsWith    => StringCall(prop, "EndsWith", v),
            FilterOperator.Equals      => Expression.Equal(prop, v),
            FilterOperator.NotEquals   => Expression.NotEqual(prop, v),
            _                          => Expression.Constant(true)
        };
    }

    private static Expression NullableIntExpr(Expression prop, FilterOperator op, string val)
    {
        if (!int.TryParse(val, out var target)) return Expression.Constant(true);
        var hasValue = Expression.Property(prop, "HasValue");
        var value    = Expression.Property(prop, "Value");
        var cmp      = NumericCmp(value, op, Expression.Constant(target));
        return op == FilterOperator.NotEquals
            ? Expression.OrElse(Expression.Not(hasValue), cmp)
            : Expression.AndAlso(hasValue, cmp);
    }

    private static Expression NullableDecimalExpr(Expression prop, FilterOperator op, string val)
    {
        if (!decimal.TryParse(val, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var target))
            return Expression.Constant(true);
        var hasValue = Expression.Property(prop, "HasValue");
        var value    = Expression.Property(prop, "Value");
        var cmp      = NumericCmp(value, op, Expression.Constant(target));
        return op == FilterOperator.NotEquals
            ? Expression.OrElse(Expression.Not(hasValue), cmp)
            : Expression.AndAlso(hasValue, cmp);
    }

    private static Expression IntExpr(Expression prop, FilterOperator op, string val)
    {
        if (!int.TryParse(val, out var target)) return Expression.Constant(true);
        return NumericCmp(prop, op, Expression.Constant(target));
    }

    private static Expression NumericCmp(Expression field, FilterOperator op, Expression target) => op switch
    {
        FilterOperator.Equals             => Expression.Equal(field, target),
        FilterOperator.NotEquals          => Expression.NotEqual(field, target),
        FilterOperator.GreaterThan        => Expression.GreaterThan(field, target),
        FilterOperator.LessThan           => Expression.LessThan(field, target),
        FilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(field, target),
        FilterOperator.LessThanOrEqual    => Expression.LessThanOrEqual(field, target),
        _                                 => Expression.Constant(true)
    };

    private static Expression TagExpr(
        ParameterExpression param, string collectionProp, string tagNavProp,
        FilterOperator op, string val)
    {
        var collection = Expression.Property(param, collectionProp);
        var itemType   = collection.Type.GetGenericArguments()[0];
        var itemParam  = Expression.Parameter(itemType, "x");
        var nameProp   = Expression.Property(Expression.Property(itemParam, tagNavProp), "Name");
        var v          = Expression.Constant(val);

        Expression matchBody = op switch
        {
            FilterOperator.Contains or FilterOperator.NotContains => StringCall(nameProp, "Contains", v),
            FilterOperator.StartsWith => StringCall(nameProp, "StartsWith", v),
            FilterOperator.EndsWith   => StringCall(nameProp, "EndsWith", v),
            _                         => Expression.Equal(nameProp, v)
        };

        var anyMethod = typeof(Enumerable)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .First(m => m.Name == "Any" && m.GetParameters().Length == 2)
            .MakeGenericMethod(itemType);

        var anyExpr = Expression.Call(anyMethod, collection, Expression.Lambda(matchBody, itemParam));

        return op is FilterOperator.NotContains or FilterOperator.NotEquals
            ? Expression.Not(anyExpr)
            : anyExpr;
    }

    private static MethodCallExpression StringCall(Expression target, string method, Expression arg)
    {
        var m = typeof(string).GetMethod(method, new[] { typeof(string) })!;
        return Expression.Call(target, m, arg);
    }
}
