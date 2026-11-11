//! Just enough date handling to show "3h" or "12 Mar" next to a notification.
//! GitHub sends UTC timestamps such as `2026-01-02T03:04:05Z`.

const MONTHS: [&str; 12] = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/// Seconds since 1970-01-01T00:00:00Z for a GitHub timestamp, or `None` if it is not one.
pub fn parse_timestamp(text: &str) -> Option<i64> {
    let bytes = text.as_bytes();
    if bytes.len() < 20 || bytes[4] != b'-' || bytes[7] != b'-' || bytes[10] != b'T' || bytes[13] != b':' || bytes[16] != b':' {
        return None;
    }

    let number = |range: std::ops::Range<usize>| text.get(range)?.parse::<i64>().ok();
    let (year, month, day) = (number(0..4)?, number(5..7)?, number(8..10)?);
    let (hour, minute, second) = (number(11..13)?, number(14..16)?, number(17..19)?);
    if !(1..=12).contains(&month) || !(1..=31).contains(&day) || hour > 23 || minute > 59 || second > 60 {
        return None;
    }

    Some(days_from_civil(year, month, day) * 86_400 + hour * 3_600 + minute * 60 + second)
}

/// Short age text for list rows: "now", "5m", "3h", "2d", "12 Mar", "12 Mar 2024".
/// Both arguments are seconds since the Unix epoch. Same rules as the C# `RelativeTime.Format`.
pub fn relative(then: i64, now: i64) -> String {
    let age = now - then;
    if age < 60 {
        return "now".to_string();
    }
    if age < 3_600 {
        return format!("{}m", age / 60);
    }
    if age < 86_400 {
        return format!("{}h", age / 3_600);
    }
    if age < 7 * 86_400 {
        return format!("{}d", age / 86_400);
    }

    let (year, month, day) = civil_from_days(then.div_euclid(86_400));
    let (this_year, _, _) = civil_from_days(now.div_euclid(86_400));
    let month = MONTHS[(month - 1) as usize];
    if year == this_year { format!("{day} {month}") } else { format!("{day} {month} {year}") }
}

// Howard Hinnant's civil-date algorithms (public domain): exact for the proleptic Gregorian calendar.
fn days_from_civil(year: i64, month: i64, day: i64) -> i64 {
    let year = if month <= 2 { year - 1 } else { year };
    let era = year.div_euclid(400);
    let year_of_era = year.rem_euclid(400);
    let day_of_year = (153 * (if month > 2 { month - 3 } else { month + 9 }) + 2) / 5 + day - 1;
    let day_of_era = year_of_era * 365 + year_of_era / 4 - year_of_era / 100 + day_of_year;
    era * 146_097 + day_of_era - 719_468
}

fn civil_from_days(days: i64) -> (i64, i64, i64) {
    let days = days + 719_468;
    let era = days.div_euclid(146_097);
    let day_of_era = days.rem_euclid(146_097);
    let year_of_era = (day_of_era - day_of_era / 1_460 + day_of_era / 36_524 - day_of_era / 146_096) / 365;
    let day_of_year = day_of_era - (365 * year_of_era + year_of_era / 4 - year_of_era / 100);
    let month_index = (5 * day_of_year + 2) / 153;
    let day = day_of_year - (153 * month_index + 2) / 5 + 1;
    let month = if month_index < 10 { month_index + 3 } else { month_index - 9 };
    let year = year_of_era + era * 400 + i64::from(month <= 2);
    (year, month, day)
}

#[cfg(test)]
mod tests {
    use super::*;

    // 2026-06-15T12:00:00Z
    const NOW: i64 = 1_781_524_800;

    #[test]
    fn timestamps_parse_to_unix_seconds() {
        assert_eq!(parse_timestamp("1970-01-01T00:00:00Z"), Some(0));
        assert_eq!(parse_timestamp("2026-06-15T12:00:00Z"), Some(NOW));
        assert_eq!(parse_timestamp("2024-02-29T23:59:59Z"), Some(1_709_251_199));
        assert_eq!(parse_timestamp("2026-06-15"), None);
        assert_eq!(parse_timestamp("2026-13-15T12:00:00Z"), None);
        assert_eq!(parse_timestamp("not a date at all!!!"), None);
    }

    #[test]
    fn the_largest_unit_under_a_week_is_used() {
        assert_eq!(relative(NOW + 30, NOW), "now");
        assert_eq!(relative(NOW - 59, NOW), "now");
        assert_eq!(relative(NOW - 5 * 60, NOW), "5m");
        assert_eq!(relative(NOW - 3 * 3_600, NOW), "3h");
        assert_eq!(relative(NOW - 2 * 86_400, NOW), "2d");
        assert_eq!(relative(NOW - (6 * 86_400 + 86_399), NOW), "6d");
    }

    #[test]
    fn older_dates_show_day_and_month_and_the_year_when_it_differs() {
        assert_eq!(relative(parse_timestamp("2026-03-12T00:00:00Z").unwrap(), NOW), "12 Mar");
        assert_eq!(relative(parse_timestamp("2024-03-12T00:00:00Z").unwrap(), NOW), "12 Mar 2024");
        assert_eq!(relative(parse_timestamp("2025-12-31T23:59:59Z").unwrap(), NOW), "31 Dec 2025");
    }
}
