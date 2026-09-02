function parseDate(
    value) {

    if (!value) {
        return null;
    }

    const date =
        new Date(
            value);

    if (Number.isNaN(
        date.getTime())) {

        return null;
    }

    return date;
}

const localDateFormatter =
    new Intl.DateTimeFormat(
        "en-US",
        {
            year:
                "numeric",

            month:
                "short",

            day:
                "numeric"
        });

const localDateTimeFormatter =
    new Intl.DateTimeFormat(
        "en-US",
        {
            year:
                "numeric",

            month:
                "short",

            day:
                "numeric",

            hour:
                "numeric",

            minute:
                "2-digit",

            hour12:
                true,

            timeZoneName:
                "short"
        });

export function formatLocalDate(
    value) {

    const date =
        parseDate(
            value);

    if (!date) {
        return null;
    }

    return localDateFormatter
        .format(
            date);
}

export function formatLocalDateTime(
    value) {

    const date =
        parseDate(
            value);

    if (!date) {
        return null;
    }

    const parts =
        localDateTimeFormatter
            .formatToParts(
                date);

    const getPart =
        type =>
            parts.find(
                part =>
                    part.type ===
                    type)?.value ?? "";

    const month =
        getPart(
            "month");

    const day =
        getPart(
            "day");

    const year =
        getPart(
            "year");

    const hour =
        getPart(
            "hour");

    const minute =
        getPart(
            "minute");

    const dayPeriod =
        getPart(
            "dayPeriod");

    const timeZoneName =
        getPart(
            "timeZoneName");

    const zoneSuffix =
        timeZoneName
            ? ` ${timeZoneName}`
            : "";

    return `${month} ${day}, ${year} ${hour}:${minute} ${dayPeriod}${zoneSuffix}`.trim();
}
