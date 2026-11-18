import { EmptyState } from '../components/EmptyState';

export interface PlaceholderPageProps {
  title: string;
  /** Which sprint/phase delivers this screen, shown to stakeholders. */
  plannedFor?: string;
}

/** Valid landing page for nav items whose feature is not built yet (Sprint 1 rule:
    every nav item lands on a real or labeled placeholder page). */
export function PlaceholderPage({ title, plannedFor = 'a later sprint' }: PlaceholderPageProps) {
  return (
    <>
      <div className="phead">
        <div>
          <h1>{title}</h1>
          <p>This area is planned and not yet implemented.</p>
        </div>
      </div>
      <EmptyState
        title={`${title} is coming in ${plannedFor}`}
        description="The navigation entry is real; the feature ships in a subsequent sprint per the delivery plan."
      />
    </>
  );
}
