type StatusBadgeProps = {
    status?: "Finalized" | "Draft" | "Deprecated" | "Planned Upgrade";
    since?: string;
};

export default function StatusBadge({ status, since }: StatusBadgeProps) {
    if (!status && !since) {
        return null;
    }

    const baseClasses =
        "inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold uppercase tracking-wide mr-2";

    const statusColors = {
        Finalized: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200",
        "Planned Upgrade": "bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200",
        Draft: "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200",
        Deprecated: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200",
    };

    const sinceClasses = "bg-gray-100 text-gray-800 dark:bg-gray-700 dark:text-gray-200";

    const statusColorClass = status ? statusColors[status] || sinceClasses : '';

    return (
        <div className="flex items-center gap-2 -mt-2 mb-4">
            {status && (
                <span className={`${baseClasses} ${statusColorClass}`}>{status}</span>
            )}
            {since && (
                <span className={`${baseClasses} ${sinceClasses}`}>Since: {since}</span>
            )}
        </div>
    );
}
