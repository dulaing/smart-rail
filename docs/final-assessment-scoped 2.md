# Individual Cross-Practice AI-Assisted Software Development Assessment

## SmartRail – Public Railway Passenger & Operations Platform

# 1. Assessment Overview

As part of the cross-practice software development training program, you are expected to independently design and build a solution for a fictional public railway transportation organization.

This is an **individual assessment**.

The purpose of this assessment is **not simply to implement a predefined application**.

The assessment evaluates your ability to independently:

- Understand an initial business problem.
- Discover and refine requirements.
- Identify missing information and assumptions.
- Use AI assistants and agents effectively throughout the development lifecycle.
- Design appropriate user experiences.
- Design a maintainable software architecture.
- Implement backend, web, and mobile solutions.
- Integrate with external systems.
- Establish appropriate quality assurance practices.
- Consider security, reliability, accessibility, observability, and operational requirements.
- Make technical decisions and justify them.
- Work across multiple software engineering disciplines.

You are expected to treat the information in this document as an **initial business brief rather than a complete requirements specification**.

Part of your responsibility is to identify what is missing.

Although the assessment covers multiple engineering disciplines, you are not expected to demonstrate expert-level depth in every area. You are expected to demonstrate that you can understand each area, make reasonable decisions, use appropriate AI assistance, and produce a coherent end-to-end solution.

---

# 2. Business Scenario

A national railway organization operates passenger train services across a network of railway stations.

Thousands of passengers use these services every day.

The organization wants to modernize the passenger experience and improve visibility into railway operations through a new digital platform called **SmartRail**.

The platform should provide passengers with accurate information about:

- Train services.
- Routes.
- Stations.
- Upcoming trains.
- Expected arrival and departure times.
- Current train locations where appropriate.
- Service disruptions and changes.

The organization also wants digital systems for:

- Passengers.
- Train drivers and operational staff.
- Railway stations.
- Central railway operations.
- External service providers.

The complete solution will therefore contain multiple applications and services rather than being a single application.

---

# 3. Passenger Experience

Passengers should have access to a mobile application that helps them understand the railway network and their journeys.

A passenger may want to understand questions such as:

- Which train should I take?
- When is the next train?
- Is my train currently running on time?
- Where is the train now?
- Which stations will the train stop at?
- When will it reach my station?
- Are there service disruptions?
- Has the platform or departure time changed?
- What are the available connections from another station?

The passenger application should provide an intuitive experience suitable for everyday commuters as well as occasional travelers.

You should determine what information and functionality would be valuable to passengers.

---

# 4. Railway Network

The railway organization manages a network containing concepts such as:

- Railway stations.
- Train services.
- Railway routes.
- Train schedules.
- Stops.
- Platforms.
- Train vehicles.
- Drivers or operators.
- Service calendars.
- Operational journeys.

A train may travel through multiple stations as part of a journey.

Different services may operate:

- At different times.
- On different days.
- On different routes.
- With different stopping patterns.

You should determine an appropriate domain model for representing the railway network and its operations.

---

# 5. Live Train Information

An important part of the system is providing passengers with information about active train journeys.

Where appropriate, passengers should be able to understand the current progress of a train.

For example, the platform may need to determine:

- Where a train currently is.
- Which station it has most recently passed.
- Which station it will arrive at next.
- Whether it is running early, on time, or late.
- Its estimated arrival time.
- Whether a journey has been cancelled or interrupted.

The mechanism used to obtain, calculate, communicate, and update live information should be investigated and designed by you.

---

# 6. Railway Station Digital Displays

Railway stations should contain digital passenger-information displays.

These displays may appear on:

- Platforms.
- Station entrances.
- Waiting areas.
- Large information screens.
- Smaller terminal devices.

A display may show information such as upcoming departures and arrivals.

However, you should investigate what information is most important in different locations.

For example, a platform display and a main station information board may have different responsibilities.

The solution should consider how these displays receive updated information and how they behave when connectivity is unreliable.

---

# 7. Station Terminal Application

In addition to passive digital displays, selected railway stations may contain interactive public terminals.

These terminals should allow passengers to explore railway information without requiring them to install the passenger mobile application.

The terminal experience may include capabilities such as:

- Searching for destinations.
- Exploring routes.
- Finding upcoming trains.
- Viewing station information.
- Viewing disruptions.
- Exploring available passenger services.

The terminal should be designed as an application suitable for deployment on dedicated station devices.

You should determine the appropriate technology and interaction model.

---

# 8. Driver / Train Operator Application

Train drivers or relevant train operational personnel should have access to a dedicated application.

The application should support their operational responsibilities during a train journey.

Possible information could include:

- Assigned journeys.
- Route information.
- Scheduled station stops.
- Timing information.
- Operational notifications.
- Journey status.

The exact responsibilities of this application are intentionally not completely specified.

You should investigate what information the driver or train operator would reasonably require and which information they should be permitted to update.

---

# 9. Railway Operations Platform

The railway organization requires a web-based system for managing and overseeing railway operations.

The users of this system should not simply be treated as a single **Administrator** role.

A real transportation organization may contain multiple responsibilities and levels of access.

Examples could include people responsible for:

- Network operations.
- Scheduling.
- Stations.
- Train services.
- Customer information.
- Incident management.
- Reporting.
- System administration.

You should identify appropriate user roles and responsibilities.

The operations platform should allow authorized users to understand what is currently happening across the railway network.

---

# 10. Service Disruptions

Railway operations do not always follow the original schedule.

Situations may include:

- Train delays.
- Train cancellations.
- Platform changes.
- Temporary station closures.
- Route changes.
- Unexpected train termination.
- Operational incidents.

The system should provide an appropriate mechanism for handling disruptions and communicating relevant information to affected users.

You should determine how disruption information propagates through the different applications.

---

# 11. External Catering Service Integration

The railway organization works with an external catering provider.

The catering provider operates its own platform and is responsible for managing food services.

SmartRail must integrate with this external platform.

The catering provider may require railway information such as:

- Train services.
- Train routes.
- Station information.
- Scheduled arrival times.
- Actual or estimated arrival times.
- Service changes.

The integration should allow the catering platform to coordinate food preparation and collection with train journeys.

---

# 12. Passenger Food Ordering

Passengers using SmartRail should be able to discover available food options associated with their journey.

A passenger should be able to select food and arrange to receive or collect the order at an appropriate point during the journey.

For example, a passenger travelling through several stations may choose an eligible station where their order can be collected.

The exact ordering and fulfillment workflow is deliberately incomplete.

You should investigate questions such as:

- Which stations support catering?
- When can an order be placed?
- How close to arrival can an order be modified?
- What happens if a train is delayed?
- What happens if the passenger misses the collection station?
- Can an order be cancelled?
- Who owns payment processing?
- Who owns the authoritative food menu?
- How are food availability changes communicated?
- How are orders associated with passengers and journeys?
- How is successful collection confirmed?

The catering provider should remain a **separate external system**.

Your architecture should reflect this separation.

---

# 13. Notifications

Different users may need to receive information when important events occur.

---

# 14. Identity and Access

Not every application has the same authentication requirements.

For example:

- Some railway information may be publicly accessible.
- Some passenger functionality may require an account.
- Driver applications require authenticated operational users.
- Railway management functionality requires organizational access.
- Different organizational roles may require different permissions.

You should define an appropriate identity and authorization model.

---

# 15. UI/UX and Design System

The solution contains several applications serving very different audiences.

These may include:

1. Passenger Mobile Application
2. Driver / Operator Application
3. Station Terminal Application
4. Station Information Displays
5. Railway Operations Web Application

You should determine whether and how these applications share a common design language.


Special attention should be paid to designing experiences that passengers can understand quickly while moving through a busy railway station.

---

# 16. Connectivity and Resilience

Some applications may operate in environments where network connectivity is unreliable.

Examples include:

- Trains travelling through areas with weak connectivity.
- Station terminals temporarily losing their network connection.
- Mobile passengers with intermittent connectivity.

You should determine how relevant applications behave under these conditions.

---

# 17. Security

Security should be incorporated throughout the development lifecycle.

Security should not be treated as an activity performed only after development is completed.

---

# 18. Quality Assurance

The project should demonstrate a structured quality engineering approach.


You should decide which tests provide the most value and justify the selected approach.

---

# 19. Observability and Operations

The railway organization must be capable of operating the platform in production.


---

# 20. Data and Privacy

You must identify what information the platform collects and classify the data appropriately.

Consider questions such as:

- Which data is public?
- Which data belongs to passengers?
- Which data is operational?
- What information should be retained?
- What should appear in logs?
- What data should be shared with the catering provider?
- What information should different organizational roles be able to view?

Important privacy and data-handling decisions should be documented.

---

# 21. AI-Assisted Development

AI usage is an important part of this assessment.

You are encouraged to use appropriate AI assistants, coding agents, research agents, design agents, testing agents, or agentic workflows throughout the project.

However, **AI-generated output should not automatically be treated as correct**.

You remain personally responsible for all decisions and delivered software.

Examples of appropriate AI-assisted activities include:

- Requirement discovery.
- Stakeholder simulation.
- Identifying missing requirements.
- User-story generation.
- Acceptance-criteria generation.
- Domain modeling.
- Architecture exploration.
- Threat modeling.
- UI/UX ideation.
- Code generation.
- Code review.
- Test-case generation.
- Accessibility reviews.
- Security reviews.
- Documentation.
- Root-cause analysis.

You should demonstrate where AI was used, how generated output was validated, and where your own judgment changed or rejected AI suggestions.

Using AI successfully does not mean accepting everything generated by an AI system.

An important part of the assessment is demonstrating that you understand the output produced by the tools you use.

---

# 22. Requirement Discovery Challenge

A significant part of this assessment is discovering what has **not** been explicitly specified.

You should not assume that this document contains everything required to build a real railway platform.

You should identify:

- Missing actors.
- Missing business processes.
- Missing failure scenarios.
- Missing states.
- Missing security requirements.
- Missing operational requirements.
- Missing accessibility requirements.
- Missing integration cases.
- Conflicting requirements.
- Ambiguous requirements.
- Important assumptions.

You may use AI agents to simulate discussions with roles such as:

- Passenger.
- Driver.
- Station manager.
- Railway controller.
- Customer-service representative.
- Catering provider.
- Security engineer.
- QA engineer.
- Accessibility specialist.
- Product owner.

You should consolidate these discoveries into your own refined requirements.

The quality of this discovery process forms part of the assessment.

---

# 23. Expected Assessment Artifacts

Your submission should demonstrate your complete journey from an incomplete business problem to a working software solution.

At minimum, provide evidence covering the following areas.

### Requirements

- Refined requirements.
- Identified assumptions.
- User personas or actors.
- User journeys.
- User stories or equivalent requirements.
- Acceptance criteria.
- Identified edge cases.

### Product and UX

- User flows.
- Wireframes.
- High-fidelity designs where appropriate.
- Design-system definition.
- Accessibility considerations.

### Architecture

- High-level solution architecture.
- Application boundaries.
- Major system components.
- Data ownership.
- Integration architecture.
- Authentication and authorization approach.
- Important architecture decisions and trade-offs.

### Backend

A working backend demonstrating appropriate:

- Domain modeling.
- APIs.
- Data persistence.
- Validation.
- Authorization.
- Integration.
- Error handling.
- Observability.

### Web Application

A web-based operational experience demonstrating meaningful railway-management functionality.

### Passenger Mobile Application

A mobile experience supporting important passenger journeys.

### Operational Mobile Application

A mobile application or equivalent experience for train drivers or operational personnel.

### Station Experience

A functional prototype representing either or both:

- Station passenger-information displays.
- Interactive station terminals.

### External Integration

A working integration with a simulated external catering platform.

The external platform may be mocked, simulated, or implemented as a small independent application where appropriate.

### QA

- Test strategy covering full spectrum of quality
- Functional test scenarios.
- Automated tests - Automating selected 5 end to end flows as they feel like most critical
- Identified defects.
- Evidence of testing.
- Quality assessment of the final product.

### Security

- Basic threat model.
- Important security decisions.
- Identified risks.
- Security controls implemented.
- Evidence of relevant security testing.

### AI Usage

Document:

- Which AI tools or agents you used.
- What responsibilities were assigned to them.
- Important prompts or workflows.
- How their output was validated.
- Examples where AI output was incorrect or insufficient.
- Examples where your own judgment improved the result.
- Any AI-generated implementation you significantly modified and why.

---

# 24. Technical Freedom

The goal of the assessment is not to force every candidate toward an identical architecture.

You are expected to make and justify technical decisions.

Technologies covered during the training may include areas such as:

- Backend development.
- Node.js.
- React.
- Flutter.
- Cross-platform mobile development.
- Web technologies.
- Databases.
- Automated testing.
- UI/UX tooling.
- AI-assisted development.

You may select technologies appropriate to your proposed solution unless additional constraints are provided by the assessors.

Choosing a technology is not sufficient by itself.

You should be able to explain **why it is appropriate for the problem being solved**.

---

# 25. Individual Scope Management

SmartRail could become an extremely large system.

You are **not expected to implement every possible railway capability**.

A major part of the assessment is demonstrating that you can manage scope responsibly as an individual engineer.

You should:

1. Explore the broader problem.
2. Identify the important capabilities.
3. Define an achievable assessment scope.
4. Prioritize requirements.
5. Identify what belongs in your MVP.
6. Clearly document what has been deferred.
7. Explain why particular capabilities were prioritized.
8. Build the selected scope to an appropriate level of quality.

A smaller, coherent, well-engineered solution is preferable to a large collection of incomplete features.

You should not reduce the assessment to a single application simply because the overall domain is large.

Instead, demonstrate that you understand the broader ecosystem while choosing an achievable implementation scope.

---

# 26. Assessment Focus

The assessment evaluates both the **product you create** and the **engineering process you personally use to create it**.

Important areas include:

| Area | What We Are Looking For |
|---|---|
| Requirement Discovery | Ability to transform an incomplete problem into meaningful requirements |
| AI-Assisted Engineering | Effective use, orchestration, and validation of AI assistants or agents |
| Product Thinking | Understanding users, priorities, workflows, and business value |
| UI/UX | Usability, consistency, accessibility, and design-system thinking |
| Architecture | Appropriate boundaries, responsibility separation, and technical decisions |
| Backend Engineering | Maintainability, correctness, APIs, data design, and business logic |
| Frontend Engineering | Usable, maintainable, responsive application development |
| Mobile Engineering | Appropriate mobile experience and platform considerations |
| Integration | Robust communication with the external catering platform |
| QA | Meaningful testing strategy and evidence of quality |
| Security | Secure-by-design engineering practices |
| Operations | Observability, reliability, and production-readiness thinking |
| Scope Management | Ability to prioritize and deliver a coherent solution individually |
| Technical Judgment | Ability to make and justify decisions instead of blindly following AI suggestions |
| Communication | Ability to explain requirements, decisions, trade-offs, and outcomes |

The assessment is not intended to determine whether you can personally produce production-level implementations in every engineering discipline.

It is intended to determine whether you can **understand and navigate the complete software development lifecycle using modern engineering and AI-assisted development practices**.

---

# 27. Final Demonstration

At the end of the assessment, you should present the solution as if demonstrating it to stakeholders from the railway organization.

The presentation should tell a story rather than simply demonstrate individual screens.

You should also explain:

- What requirements you discovered.
- What assumptions you made.
- What you intentionally excluded.
- How you prioritized the scope.
- Important architecture decisions.
- Important UX decisions.
- Security considerations.
- QA strategy.
- How AI contributed to the development process.
- Where AI-generated suggestions were challenged or rejected.
- What you would improve if the system were continued beyond the assessment.

---

# 28. Individual Ownership

This assessment is intended to evaluate your individual engineering capability.

You may use:

- AI assistants.
- Coding agents.
- Design tools.
- Automated testing tools.
- Documentation tools.
- Development frameworks and libraries.
- Other approved engineering tools.

However, the submitted work must represent **your own understanding and decision-making**.

During the assessment or final review, you may be asked to:

- Explain generated code.
- Modify an existing feature.
- Debug an issue.
- Explain an architectural decision.
- Change a requirement.
- Explain your data model.
- Demonstrate how an integration works.
- Explain why a particular AI recommendation was accepted or rejected.
- Describe alternative approaches you considered.

You should therefore avoid including functionality that you cannot explain or maintain yourself.

---