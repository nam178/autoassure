import { AppShell, NavLink } from "@mantine/core";
import { memo } from "react";
import { Link, Outlet, useLocation } from "react-router-dom";
import { ROUTE_HOME, ROUTE_SCENARIOS } from "../../common/Config";

/** Sidebar entries, in display order. Add a route here to expose it in the nav. */
const NAV_ITEMS: { readonly path: string; readonly label: string }[] = [
  { path: ROUTE_HOME, label: "Home" },
  { path: ROUTE_SCENARIOS, label: "Scenarios" },
];

// Shell every logged-in page renders inside: a fixed navbar for switching
// pages, and an outlet that swaps in the current route's page as the body.
export const AppLayout = memo(function AppLayout() {
  const location = useLocation();

  return (
    <AppShell navbar={{ width: 240, breakpoint: "sm" }} padding="md">
      <AppShell.Navbar p="md">
        {NAV_ITEMS.map((item) => (
          <NavLink
            key={item.path}
            component={Link}
            to={item.path}
            label={item.label}
            active={location.pathname === item.path}
          />
        ))}
      </AppShell.Navbar>
      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  );
});
