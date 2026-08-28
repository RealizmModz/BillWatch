using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Statements;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BillWatch.Tests.Services;

public sealed class TesseractEndToEndOcrTests
    : IClassFixture<BillWatchApiFactory>
{
    private static readonly TimeSpan ProcessingTimeout =
        TimeSpan.FromSeconds(20);

    private readonly BillWatchApiFactory
        _factory;

    public TesseractEndToEndOcrTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    [Trait("Category", "NativeOcr")]
    public async Task
        PngStatement_OcrsParsesAndPersists()
    {
        /*
         * BillWatchApiFactory normally replaces the native OCR engine
         * with a fast deterministic fake.
         *
         * This derived host restores the real production Tesseract
         * implementation for this one integration test only.
         */
        using var nativeFactory =
            _factory.WithWebHostBuilder(
                builder =>
                {
                    builder.ConfigureServices(
                        services =>
                        {
                            services.RemoveAll<
                                IBillStatementOcrEngine>();

                            services.AddSingleton<
                                IBillStatementOcrEngine,
                                TesseractBillStatementOcrEngine>();
                        });
                });

        using var client =
            nativeFactory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    BaseAddress =
                        new Uri(
                            "https://localhost"),

                    AllowAutoRedirect =
                        false
                });

        var user =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            user);

        var billStreamId =
            await CreateBillStreamAsync(
                client);

        var statementImage =
            Convert.FromBase64String(
                StatementPngBase64);

        var uploadId =
            await UploadStatementAsync(
                client,
                billStreamId,
                statementImage);

        await WaitForProcessedAsync(
            nativeFactory.Services,
            uploadId);

        using var scope =
            nativeFactory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var upload =
            await dbContext
                .BillStatementUploads
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        uploadId);

        Assert.Equal(
            BillStatementUploadStatus.Processed,
            upload.Status);

        Assert.True(
            upload.BillStatementId.HasValue);

        var statement =
            await dbContext
                .BillStatements
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        upload.BillStatementId.Value);

        Assert.Equal(
            billStreamId,
            statement.BillStreamId);

        Assert.Equal(
            new DateOnly(
                2026,
                7,
                10),
            statement.PeriodStart);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                9),
            statement.PeriodEnd);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                10),
            statement.StatementDate);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                31),
            statement.DueDate);

        Assert.Equal(
            94.99m,
            statement.TotalAmount);

        Assert.Equal(
            "USD",
            statement.CurrencyCode);

        var statementCount =
            await dbContext
                .BillStatements
                .AsNoTracking()
                .CountAsync(
                    item =>
                        item.BillStreamId ==
                        billStreamId);

        Assert.Equal(
            1,
            statementCount);
    }

    private static async Task<Guid>
        CreateBillStreamAsync(
            HttpClient client)
    {
        using var response =
            await client.PostAsJsonAsync(
                "/api/bill-streams",
                new
                {
                    providerName =
                        $"Native OCR Test {Guid.NewGuid():N}",

                    category =
                        "Internet"
                });

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<
                    BillStreamPayload>();

        return result?.Id
            ?? throw new InvalidOperationException(
                "The bill stream response was empty.");
    }

    private static async Task<Guid>
        UploadStatementAsync(
            HttpClient client,
            Guid billStreamId,
            byte[] imageBytes)
    {
        using var multipart =
            new MultipartFormDataContent();

        using var fileContent =
            new ByteArrayContent(
                imageBytes);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue(
                "image/png");

        multipart.Add(
            fileContent,
            "file",
            "ocr-statement.png");

        using var response =
            await client.PostAsync(
                $"/api/bill-streams/{billStreamId}/statement-uploads",
                multipart);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var result =
            await response.Content
                .ReadFromJsonAsync<
                    StatementUploadPayload>();

        return result?.Id
            ?? throw new InvalidOperationException(
                "The statement upload response was empty.");
    }

    private static async Task WaitForProcessedAsync(
        IServiceProvider services,
        Guid uploadId)
    {
        var deadline =
            DateTimeOffset.UtcNow +
            ProcessingTimeout;

        BillStatementUploadStatus?
            lastStatus =
                null;

        while (DateTimeOffset.UtcNow <
               deadline)
        {
            using var scope =
                services.CreateScope();

            var dbContext =
                scope.ServiceProvider
                    .GetRequiredService<
                        BillWatchDbContext>();

            lastStatus =
                await dbContext
                    .BillStatementUploads
                    .AsNoTracking()
                    .Where(
                        upload =>
                            upload.Id ==
                            uploadId)
                    .Select(
                        upload =>
                            (BillStatementUploadStatus?)
                            upload.Status)
                    .SingleOrDefaultAsync();

            if (lastStatus ==
                BillStatementUploadStatus.Processed)
            {
                return;
            }

            if (lastStatus is
                BillStatementUploadStatus.Failed or
                BillStatementUploadStatus.NeedsOcr or
                BillStatementUploadStatus.ReadyForParsing)
            {
                break;
            }

            await Task.Delay(
                100);
        }

        throw new TimeoutException(
            $"Native OCR statement upload {uploadId} did not reach Processed. Last status: {lastStatus?.ToString() ?? "not found"}.");
    }

    private sealed class BillStreamPayload
    {
        public Guid Id { get; set; }
    }

    private sealed class StatementUploadPayload
    {
        public Guid Id { get; set; }
    }

    /*
     * Deterministic 1700x800 monochrome PNG.
     *
     * Visible text:
     *
     * BILLWATCH OCR TEST
     * Statement Date: 08/10/2026
     * Billing Period: 07/10/2026 - 08/09/2026
     * Due Date: 08/31/2026
     * Total Amount Due: $94.99
     *
     * Keeping the image embedded avoids:
     *
     * - another image-generation package,
     * - system-font dependencies,
     * - runtime test asset paths,
     * - extra project content rules,
     * - additional restore/build overhead.
     */
    private const string StatementPngBase64 =
        """
        iVBORw0KGgoAAAANSUhEUgAABqQAAAMgAQAAAABtsj78AAAUdUlEQVR42u3dPXPcSGLG8X9jYBKuYomwI5ZLFnEu52Y52mBXxK0v
        90dYVdkfQCF9pZOaOpWPjnypM56/gfPzLahV1TmkM2cG6Q0mxGwxwIwxaAd4GWCGQ5HH1RCUn07mDUPhN93oF+xWP8bx+ZWJx+dY
        pJJKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmkkur/hWpu2qdn
        EWDipQOOw4GxXFMSdl22m+06hytGzr7JdkveJIBL2c92nX3jXMnIuZztApxzKXAEuyW8BrbdQErWrasflsUlCQCnjOt3ZsxhzAyI
        FsfNhtwCnW3fLbFJ1jar9n3GEJM2T5uSD/q6StY00sXTFNLVw7JH1VsUVbWUPXdWV13xWPrAdB1uUVv7CY7DpG9Nd7PBqg4Xl49x
        jpQIOMy3XT5qG519EeUlyQGjHNgvRs5uu4yDXeOSw3yQdfWi/1FKXl8xPgdtx7EHhh3w+wfbRzcKZwHBot8oCg9v4JOSG08u26sa
        1WLmYPBXqunxqCw7d+kAzgesOu1MF15DEq90jaWHV17TKfwwXNXZyhWf/mU1KEXAxLQfBzWwKlMTRS4acAuM7/DF/iD8/XBVpltZ
        f/qRxUnQ6TNC5o9hdkv8W3Jrk6/L9d8Mek8HPF4ld/nmweKpf8evPuw80B7fbgeg0bBqqj8P7P3eP1k6cNfZ68eobZdytG5iPIC6
        ihYDb/Rf/K9ZV4f5ylJxe8CjcLiyWjTY3lreKym9AGDUlybDVd2p7PUGrOiRqGJ73QreUdQD8KCnuGtUYUbplXXTCvPmIjKA75eU
        S2vhIauy9bOjdlIej8Fx1f/i90NzeTcMV0X9XjBvZ4g2TQOP+Jzenae5TaKhqs7iEJiYCONVU91tl1aX0KKTv4gxnMUs3pqakGP7
        KK6rALvoD7zerD5s5nz2kfQW0epom9GpFoggqptjHxUPVxWv1Fi4BNiDpMEn/VsCg1WtO7UX7NXPthjBHlv9n+CA0XBbYGc25xsi
        sma297TtHk3yFLb5ot9l7vJ0WCqj3UYfTZFKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmk
        kkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSqpBqXqb+H68TKPFXmEuAJwH8xDngTNDUU05uelAs1yr4zFzYqb2
        wjKbweWli7m64tLFzB5437PFueYU8xjgw8pBIUC1A/akffN0xhXn5C6CsbOkKSnnc1LSB4/iWKiyNx85dNx/+ctvyV7n8HMgzRI8
        ioyz1FJknJdDUV3Y9KYDd4ulqI2dAzgG2IPRDjAa5WztMRrljO54jX46ldduPHd9Gb3uvXRhYC94RWZS+OClJDtYTvxkB8uHwVxX
        Hz622es313w5xmzVL/yw2jI2pLi53xnWeBX2WlUZ+Un74sRkEBG70EujAez7uKw6jgCOwQUuwPkwieqPAi4C6LyxtrhwSKrvzw7t
        2H3lOxvP/3r2w4zZnMn5eDqpzjI5nfFrw3mvM7xs62vIM6YCmJGSucwxxpI0fZ9f73EbF3yAIvbSTt0c1c3SZMNSVSe//xrIn2f8
        T/p3qU3zhH9e9H3bqQW3/bvm9bNrRmyA8s+Doaiew9PDJP3GfFdkZ0fshDsh322lhPF2FZnkpV/sJS9dufdXbR1h251UHe2uqglX
        s6GoXAQYQsBQQJAE+GREi84h9gGidq/V8E2wGA7Cdh9wn8wNZRSOLjo9QEzoh35yYiBe2o+0iNvmFbztd3a2ebjIkoGowmp33qD9
        les6sp0zbzqIL6uHLV609VbW6xFwAVs7Q2mBO4vpa8IzoupaOVr5Rk47/M44XcSKRJSVvwwJHnhr84Vq1HbhvWtltSwumfHr8TCX
        2J1//nkJ9fRh3fJ36WRNfwfzImoewgeOBVxe4UcAU1NfUxf9FcV/VMf5i17ly3ZyVMTk1QQwj3vbhD/03OKb3gb515Rk5XVvdZjZ
        7sOAZkzl7k2L818uLXH9/o7uF72HoahC6kyhbVef/r7Lu2uPqr92Lll0/Y0zyOF99UfeD252W8S3XWD4SdnSTIbF8g4vdfDu4W8y
        3uEEPhYRuhiEh7cSyRdTB5aWFTFlb/LRo2ZNYyx5+LhXr9OhfeRc+v2IlxZxQVot549caChCTFZGRYh56NXwQpVWg0wGPGveO1rq
        uAugc7si7lV1HrUPw7nLOSYFkpQ6Y6hqX536+w1ZXvHtBzBZbpusaL4sI8hiOCriLAZbRgNRTfnaS/004RlhXM0i+KoakI4A5m9N
        nhFYbzwF4Oqc/Tfvcgswv+KS1MJsXD1cDaa3MD4XX1+c4PzgrO7go2rmMw1h4vv8w1/AsZlWndwvfkr4NoB5cEwUxtUfOIwsxucw
        jAeiitkzGEJCmhuXzd3cuH4R+524v3irTVEhbOaLURUscjCYO9JfkpKQpOxg7NNmWfiSZsJhUg6+YAQHVeTfFwHbpNV198SrrU+r
        me/O1sO2wPvmiVw2/eXJEYMpn2meyH3r6v3zzvT2s6mr9vvB51RXgyzK6ZFKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJK
        KqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmkkkoqqa5RhRNOpnbloPqtKp9iEi8+WJsj0oaN
        TGKcD2Cqow13Di65l+rMxEufOXPdFjfn5xgTMOGmHJE2bOT8nNkcJhMiZjMmRMw53WRdna9W0jXfiKud2bIbc0SasBHiK8bEZBkZ
        Y2czso/uGfkjq67ZSG61rbjtar92M+OGHJEmbOTN9mvSPMXwOifNEl7njN98ctUil2Lk3i5/uP+ff7v6jb0XOEJuzhHZOYDjf6qO
        Hm0B5viPGO1gjv8IY19ssq5ekAHbnfrZSWHprTLaBSC9KUekDRspo10+kJFuEfPBS7eIuezvkvupVe7W/b4L8T6WI+IR1xsvG/CC
        ajPSgHJNqsAnU4WrH2cr7xRxZxPVdTkibdhIEQf2hLza/f3EZOGnr6e1tXEMZ7b6lecBXFjmfh3KU5/12iq8tvT7nJNNq0Jgan9l
        Y+J/qU7lasbl6dRezXG2M55Fi3Z000m2YSMf2Gi5rq5KUte0vXMHFjJsScoJ5NZPmiFnbY5IGzaS28VGsu3Rm1U1J1a8ysr9+gTO
        Muv24SJPisPOicfX/rFejkgnbKQ37rpdf6Oqdqv4txRRfd7/uJPwwvLeT/PYXnetrM0R6YaNYGmSEFJm842q/rXRGfI4qs6szmV4
        52VZ57rKO7sghmtyRHphI0H7L3nknz50aaGam3cs/uE6b2M39MBCYDDdTtle18f1c0TasJG8qcn6IcvTTdZV0M6i7Joe/Ki9Al2n
        k78+R6QNG7FYB9S/2btqBvUgs9tJRbx6hskMuJD8sjNKm86QvS5HpA0bKYCwQ/c//UbnnTwR93r5w4swjq5tc+ktfqQ2bGQ5EmcD
        ARbdXvaba2YFf3OLUe76HBFz/Mf1BTura7Kt0HyTLXB1vjPibM0P225VvpIj4uowkjZsJAriMurQr1u1fcq5xfK/tr9u2Zr0p8C2
        +7A4pu5r9rC1efHwAH3g3drtuhyRNmxka7Se/kB3zg7daocVY9tJ0NockagZGGJTfChqc7ChCJU73w8MbAFBNRO8NkfEuHQRNhLY
        ov4h3ldHv99IhM+tVetbzu1yRN71Hh5GtVv3vvWCwmQEzxa9tgsXnfNNOSJNMz3Jm7mIYyMBFr3ojFt1FElu27pblyPSho20R5cB
        HLmwDMB++tXwup7dZk0lVWCHW8wo+tPDNTkii7ARF0JRcYuQTd/l7LSfIEmb+ksBXpVhaBNOwEsnbZDe2hyRNmzESyekZVSZvyyj
        PIL40wfDdFW/6bSztB5of+5igOdFFCRVVY5PKZr/krA2R6QNGxmfMr6KJ2QxzK+qh9kG62puOvduvYu67R9WB+wHsX8WApjp26pb
        vzFHpAkbMdO3TEMLHHtEYcyxRxRs9C6n351W163koJkiWq9ud7azhlqbI9KGjdidxRy9PnpvJcntk6q+6M7iXlSPO9V1tTPC2Opk
        XobkzVpkbY5IGzbyMuTgyeLoxQzq0xa3puRv3E3lu+bJr2446GLl6M2UbO3cYkBBGj/mjOnmyXVym8PS61cuD6jKbrkWCW7zx/0N
        q9bkiZReG+ryCMu6PJHefyp4fGVNXc39UfH51RXsPOa6WqMaNZOKx1nW9U6PO5RI/3eWVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkl
        lVRSSSWVVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWVVFJJVZV5CMcWOI7qdzYaEHLX0m5ilNDZ
        gcku79OUjVzJviu+ZNu5Y+fyl+y73LiMfVew7wZVevsx9bb1CwHmcfPyfM6MMQUUTC3kmw0IuUcL7O0VtxQucpba/LCAeP93jAHs
        RgNC7li6eSKdfUC3y6XNyLb2IP4ZmPQCXrDpgJB71FXauejNt/3jTvwks8/JPUK7bSHbbEDIPVR7SefFwUq/ZojAErAHmM0GhNxD
        5aedF0HS6ylDL+1/cbMBIfdQVc/nQX3BTT2AafjRv3EyaBXZhEmbyJCOnf3+LB73OsNJ8+Ty0cwtqj7w3LUfJMBpyeVqYEjdLDcW
        EHLvGdNZsy23yVPgl//dfLBbVVC+3I1sIiDkPqojqBJEAJM92cqeHn67s9cqbELabCyYbDYg5N511SYyhKbq65K2V3yH12zD7m82
        IOS+Khf23iqjtnn5ZwFw8XXzOmkeNhAQ8mOuROq+4Fk9peKApNmCz242IOS+qpJeb1fQDr9zzvGbaW+w2YCQe9fV0lxhcclcHV4B
        +/+++kcMj6cFuuUlbpJ0J4gbDQi5/3jV/PI/AHDsLeYVzzipt4J14WYDQn68ulq+WNJ6A8i679toQMgfvGpcKak7WfoBwu43Mus6
        DwOuqxNoE0RcWK2G37h0cSOjmw+10YCQH68FdheDXuoW92iqnnKjASH3ULn+uPWRstGAkPvUVQiLQTcCC35yLbXYcEDIPVS9nJP+
        TT6TlVFJRtR8EG82IOQeqiIK7CJBJMZ1qw4iDBTNbGOzASH3UI1jSJsEkXNblNWolVwCR0XsSKFs2ttmA0LuoYosp3GdIOJ+yvMC
        LzVXcwswGxPGXxexi39tgdxuNiDkD1bNjcG/CKsEkannsR9YLuJfVKPuYWSDMx84O2ay77PhgJB71NUWHlGz2fcOocFUd5+BaIRP
        AD6dxdTmAkLuoTrC2KRJEMnYeQIJeyYBeOozsi9zRvaLxRxxgwEhdyvmTrO4yW795PIZAy4T/Xfhzhidfk4q84d9beDX1SMpuq6k
        kkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmkkkoqqaSSSiqppJJKKqmk
        kkoqqaSSSqr1qqm97dedhePmuQ8X8bBUc2N6mzau2ZHtbCURxdkYzoDZHE7PB1lXraVc3euw2tJyea/AGefVvqpjYmevHul1tbwB
        Xf689qd5Wu6+HpZq5H51m02MRm7lqDNbbeM72oKDF8Ouq2LdDo7d1BF+eMeEoroIP5AVsRt4C1zXm/VSR3497xxputflI7uueqkj
        Fi4pKSPghDy3w9hSuqc6i4FJtbljXsI8nIdMg3Y06nyjihlxWy+BiCJujMHw6io+n07C8zqb4ZTp3199f8V4xiWX7s/m7qsKlk2Y
        UMeM7D0Z/NzC1fujWoDMQVqmJfVey3nTtqqxuo0ZqT60w1WV+6+AUZYAmBB+/+3vv+W7Kl9kknV7iU7MCLgmEqaAYWzR2d/F9/n2
        bvZ0p+0O9qK9iHdVDpEXmO+qzv4IcJ2YkYG3wDzeB6LqrRQLoQ1dUHV6vr80s+pWyuWAVdjOaOMFEAVRYOt8kaC3a2UTM5JWu09/
        qK68wUQFXDNedZM3FkNxuHLkM/DGEDVcix1KrkP3XCfd3zohIQbK6Lq0kCZmZFbtZX4yqL5iua46Y+jiOjLr51OGE0KSai0Wu8Hk
        pay2wLmp21N6i6/bGRzWFTyfwfnpQFXf19V28wyxjhl56Qjeh2UEEAWxN3k3UFV6l68/wW4RVdPAPayxwUBVZw7ATbxbXSIvMbbu
        IrZG8OJgoKqPxj6dwCJmJEh4M+E4DMAUH9j/bTxM1d2SGQYXKXeNajGGlofuqJ289spj2KK5q9qtRuH+aXvp8qo9ZClm5HLbZXld
        zcNYk6zrv4t+oEanY+yljjyCusJmzWlD3MwBs5LdnjcKbBMzUlKwCPRxYSd8czCqILkA8qXUl7yIevO7KnWkihlxljiv+pcJaRmV
        A1mT9DNr33PEq7zuN+pQnld5XE+KqqyDyHIaNzEjp2Nm8/q6G1/Fzv5sEKpuz+xdvHs1Df/tT55U88Coever0E3f/6S6zVS0qSO/
        8A8BLk/dpHoG09DNj3cHV1eGgLhZC7clMvjVNRbWo7RHVMeMeIwY/F3O5IAD9p7Wa636Z9/bYlT9552d+raFsU3MiEl32BnVnfnB
        E0YDiXm4Z+6BOx7iTRnlHkgllVSb6QOHWdQHSiWVVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWV
        VFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWV
        VFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWVVFJJJZVUUkkllVRSSSWV
        VFJJJZVUUkkllVRSSSWVVFJJJdVnrPo/r6cFDZJzyH0AAAAASUVORK5CYII=
        """;
}